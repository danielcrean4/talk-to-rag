FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files first, so code changes don't re-download packages.
COPY src/TalkToRag.Domain/TalkToRag.Domain.csproj src/TalkToRag.Domain/
COPY src/TalkToRag.Application/TalkToRag.Application.csproj src/TalkToRag.Application/
COPY src/TalkToRag.Infrastructure/TalkToRag.Infrastructure.csproj src/TalkToRag.Infrastructure/
COPY src/TalkToRag.Api/TalkToRag.Api.csproj src/TalkToRag.Api/
RUN dotnet restore src/TalkToRag.Api/TalkToRag.Api.csproj

COPY src ./src
RUN dotnet publish src/TalkToRag.Api -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .

RUN mkdir -p /var/talk-to-rag/blobs && chown $APP_UID /var/talk-to-rag/blobs
USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=5000
EXPOSE 5000
ENTRYPOINT ["dotnet", "TalkToRag.Api.dll"]
