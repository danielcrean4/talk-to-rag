FROM python:3.13-slim

COPY --from=ghcr.io/astral-sh/uv:0.13.6 /uv /bin/uv

WORKDIR /app
ENV UV_COMPILE_BYTECODE=1 UV_LINK_MODE=copy UV_PYTHON_DOWNLOADS=never

# Dependencies first, so code changes don't reinstall them.
COPY pyproject.toml uv.lock ./
RUN uv sync --frozen --no-dev --extra speech --no-install-project
COPY config ./config
COPY src ./src
RUN uv sync --frozen --no-dev --extra speech

# Same uid as the .NET image's "app" user, so both can read and write the shared blob volume.
RUN useradd --system --uid 1654 app \
    && mkdir -p /var/talk-to-rag/blobs /var/talk-to-rag/models \
    && chown app /var/talk-to-rag/blobs /var/talk-to-rag/models
USER app

EXPOSE 8000
CMD ["/app/.venv/bin/uvicorn", "talk_to_rag.api.app:create_app", "--factory", "--host", "0.0.0.0", "--port", "8000"]
