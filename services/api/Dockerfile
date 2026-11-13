# Built from the repo root (compose.yaml sets the context), so both the API and web/ are visible.

# The Angular app is built here, so running the stack needs no Node on the host.
FROM node:24-bookworm-slim AS web
WORKDIR /web
COPY web/package.json web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY web/ ./
RUN npx ng build

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Project files first, so code changes don't re-download packages.
COPY services/api/src/TalkToRag.Domain/TalkToRag.Domain.csproj src/TalkToRag.Domain/
COPY services/api/src/TalkToRag.Application/TalkToRag.Application.csproj src/TalkToRag.Application/
COPY services/api/src/TalkToRag.Infrastructure/TalkToRag.Infrastructure.csproj src/TalkToRag.Infrastructure/
COPY services/api/src/TalkToRag.Api/TalkToRag.Api.csproj src/TalkToRag.Api/
RUN dotnet restore src/TalkToRag.Api/TalkToRag.Api.csproj

COPY services/api/src ./src
RUN dotnet publish src/TalkToRag.Api -c Release -o /out --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
# The production build of web/; Program.cs serves it and answers its routes with index.html.
COPY --from=web /web/dist/web/browser ./wwwroot
# The licence notices of the libraries in the bundle, which the build moves out of the scripts.
COPY --from=web /web/dist/web/3rdpartylicenses.txt ./wwwroot/

RUN mkdir -p /var/talk-to-rag/blobs && chown $APP_UID /var/talk-to-rag/blobs
USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=5000
EXPOSE 5000
ENTRYPOINT ["dotnet", "TalkToRag.Api.dll"]
