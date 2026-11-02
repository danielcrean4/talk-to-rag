using System.Data;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using Pgvector;
using TalkToRag.Application.Common.Interfaces;
using TalkToRag.Domain.Entities;

namespace TalkToRag.Infrastructure.Persistence;

public sealed class SearchRepository(TalkToRagDbContext db) : ISearchRepository
{
    public async Task<IReadOnlyList<SearchHit>> SearchAsync(SearchQueryParameters parameters, CancellationToken cancellationToken)
    {
        var includeSemantic = parameters.Mode is "hybrid" or "semantic";
        var includeKeyword = parameters.Mode is "hybrid" or "keyword";

        var embedding = parameters.QueryEmbeddings.Count > 0 ? parameters.QueryEmbeddings[0] : new float[SourceChunk.EmbeddingDimensions];
        var allowedIds = parameters.AllowedSourceIds?.ToArray() ?? [];
        var filterSources = parameters.AllowedSourceIds is not null;
        var hasMaxDist = parameters.MaxCosineDistance.HasValue;
        var maxDist = parameters.MaxCosineDistance ?? 2.0f;

        const string sql = """
            WITH semantic AS (
                SELECT c.id, 
                       c.source_id,
                       c.page_number,
                       c.page_label,
                       c.section,
                       c.chunk_index,
                       c.content,
                       s.title AS source_title,
                       s.content_type AS source_content_type,
                       (c.embedding <=> @embedding::vector) AS distance,
                       row_number() OVER (ORDER BY c.embedding <=> @embedding::vector) AS rank,
                       'semantic' AS match_type
                FROM source_chunks c
                JOIN sources s ON s.id = c.source_id
                WHERE s.user_id = @user_id
                  AND s.status = 'Ready'
                  AND (@filter_sources = FALSE OR s.id = ANY(@allowed_ids))
                  AND (@has_max_dist = FALSE OR (c.embedding <=> @embedding::vector) <= @max_dist)
                ORDER BY c.embedding <=> @embedding::vector
                LIMIT 50
            ),
            keyword AS (
                SELECT c.id, 
                       c.source_id,
                       c.page_number,
                       c.page_label,
                       c.section,
                       c.chunk_index,
                       c.content,
                       s.title AS source_title,
                       s.content_type AS source_content_type,
                       (c.embedding <=> @embedding::vector) AS distance,
                       row_number() OVER (ORDER BY ts_rank_cd(c.search_vector, q) DESC) AS rank,
                       'keyword' AS match_type
                FROM source_chunks c
                JOIN sources s ON s.id = c.source_id,
                     websearch_to_tsquery('english', @query) q
                WHERE c.search_vector @@ q
                  AND s.user_id = @user_id
                  AND s.status = 'Ready'
                  AND (@filter_sources = FALSE OR s.id = ANY(@allowed_ids))
                ORDER BY ts_rank_cd(c.search_vector, q) DESC
                LIMIT 50
            ),
            candidates AS (
                SELECT * FROM semantic WHERE @include_semantic = TRUE
                UNION ALL
                SELECT * FROM keyword WHERE @include_keyword = TRUE
            ),
            fused AS (
                SELECT id,
                       source_id,
                       page_number,
                       page_label,
                       section,
                       chunk_index,
                       content,
                       source_title,
                       source_content_type,
                       min(distance) AS distance,
                       sum(1.0 / (60.0 + rank)) AS rrf_score,
                       array_agg(DISTINCT match_type) AS matched_by
                FROM candidates
                GROUP BY id, source_id, page_number, page_label, section, chunk_index, content, source_title, source_content_type
                ORDER BY rrf_score DESC
                LIMIT @limit
            )
            SELECT f.id,
                   f.source_id,
                   f.source_title,
                   f.source_content_type,
                   f.page_number,
                   f.page_label,
                   f.section,
                   f.chunk_index,
                   f.content,
                   f.distance,
                   f.rrf_score,
                   f.matched_by,
                   CASE 
                       WHEN 'keyword' = ANY(f.matched_by) THEN 
                           ts_headline('english', f.content, websearch_to_tsquery('english', @query), 'StartSel=<mark>,StopSel=</mark>,MaxFragments=1,MaxWords=35')
                       ELSE NULL 
                   END AS headline
            FROM fused f
            ORDER BY f.rrf_score DESC;
            """;

        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
        var connection = (NpgsqlConnection)db.Database.GetDbConnection();

        if (connection.State != ConnectionState.Open)
        {
            await connection.OpenAsync(cancellationToken);
        }

        // Enable iterative index scans for filtered HNSW retrieval
        await using (var scanCmd = new NpgsqlCommand("SET LOCAL hnsw.iterative_scan = relaxed_order;", connection, (NpgsqlTransaction)transaction.GetDbTransaction()))
        {
            await scanCmd.ExecuteNonQueryAsync(cancellationToken);
        }

        var results = new List<SearchHit>();

        await using (var cmd = new NpgsqlCommand(sql, connection, (NpgsqlTransaction)transaction.GetDbTransaction()))
        {
            cmd.Parameters.Add(new NpgsqlParameter("user_id", NpgsqlDbType.Uuid) { Value = parameters.UserId });
            cmd.Parameters.Add(new NpgsqlParameter("query", NpgsqlDbType.Text) { Value = parameters.QueryText });
            cmd.Parameters.Add(new NpgsqlParameter("embedding", NpgsqlDbType.Text) { Value = new Vector(embedding).ToString() });
            cmd.Parameters.Add(new NpgsqlParameter("allowed_ids", NpgsqlDbType.Array | NpgsqlDbType.Uuid) { Value = allowedIds });
            cmd.Parameters.Add(new NpgsqlParameter("filter_sources", NpgsqlDbType.Boolean) { Value = filterSources });
            cmd.Parameters.Add(new NpgsqlParameter("include_semantic", NpgsqlDbType.Boolean) { Value = includeSemantic });
            cmd.Parameters.Add(new NpgsqlParameter("include_keyword", NpgsqlDbType.Boolean) { Value = includeKeyword });
            cmd.Parameters.Add(new NpgsqlParameter("has_max_dist", NpgsqlDbType.Boolean) { Value = hasMaxDist });
            cmd.Parameters.Add(new NpgsqlParameter("max_dist", NpgsqlDbType.Real) { Value = maxDist });
            cmd.Parameters.Add(new NpgsqlParameter("limit", NpgsqlDbType.Integer) { Value = parameters.Limit });

            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                var chunkId = reader.GetGuid(0);
                var sourceId = reader.GetGuid(1);
                var sourceTitle = reader.GetString(2);
                var sourceContentType = reader.GetString(3);
                var pageNumber = reader.GetInt32(4);
                var pageLabel = reader.IsDBNull(5) ? null : reader.GetString(5);
                var section = reader.IsDBNull(6) ? null : reader.GetString(6);
                var chunkIndex = reader.GetInt32(7);
                var content = reader.GetString(8);
                var distance = (float)reader.GetDouble(9);
                var score = reader.GetDouble(10);
                var matchedBy = (string[])reader.GetValue(11);
                var headline = reader.IsDBNull(12) ? null : reader.GetString(12);

                results.Add(new SearchHit(
                    chunkId,
                    sourceId,
                    sourceTitle,
                    sourceContentType,
                    pageNumber,
                    pageLabel,
                    section,
                    chunkIndex,
                    content,
                    headline,
                    score,
                    distance,
                    matchedBy));
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return results;
    }
}
