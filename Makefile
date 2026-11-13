.PHONY: up down logs ps migrate test test-ai test-api test-mcp health web test-web test-e2e lint sample eval

# .NET commands use the local SDK when there is one, and the SDK image otherwise.
ifeq ($(shell command -v dotnet 2>/dev/null),)
IN_SDK = docker run --rm --add-host host.docker.internal:host-gateway \
	-v /var/run/docker.sock:/var/run/docker.sock -e TESTCONTAINERS_HOST_OVERRIDE=host.docker.internal \
	-v "$(CURDIR)":/src -w /src/services/api
SDK_IMAGE = mcr.microsoft.com/dotnet/sdk:10.0
DOTNET = $(IN_SDK) $(SDK_IMAGE)
DOTNET_MIGRATE = $(IN_SDK) -e ConnectionStrings__TalkToRag="Host=host.docker.internal;Port=5432;Database=talk_to_rag;Username=talk_to_rag;Password=talk_to_rag" $(SDK_IMAGE)
else
DOTNET = cd services/api &&
DOTNET_MIGRATE = $(DOTNET)
endif

up:  ## Build and start Postgres, the AI service and the API
	docker compose up -d --build --wait

down:  ## Stop the stack (data volumes are kept)
	docker compose down

logs:
	docker compose logs -f

ps:
	docker compose ps

migrate:  ## Apply EF Core migrations to the compose Postgres
	$(DOTNET_MIGRATE) sh -c 'dotnet tool restore && dotnet ef database update \
		--project src/TalkToRag.Infrastructure --startup-project src/TalkToRag.Api'

web:  ## Serve the Angular app on http://127.0.0.1:4201 (needs the stack from `make up`)
	cd web && npm install && npx ng serve

test-web:  ## Run the Angular unit tests
	cd web && npm install && npx ng test --watch=false

test-e2e:  ## Run the browser tests against a throwaway stack (needs Node, Docker and Ollama)
	./web/e2e/run.sh

lint:  ## Check Python lint and formatting (Ruff) and Angular formatting (Prettier)
	uvx ruff@0.17.5 check services/ai services/mcp
	uvx ruff@0.17.5 format --check services/ai services/mcp
	cd web && npm install && npm run format:check

test: test-ai test-api test-mcp

test-ai:
	cd services/ai && uv run pytest -q

test-api:
	$(DOTNET) dotnet test

test-mcp:  ## The database checks are skipped unless the compose Postgres is up
	cd services/mcp && uv run pytest -q

health:  ## Ask each service if it's up
	@curl -fsS http://127.0.0.1:8000/health && echo
	@curl -fsS http://127.0.0.1:5000/api/health && echo

sample:  ## Load four public-domain texts to try the app with
	./sample/load.sh

eval:  ## Score retrieval on the sample documents in a throwaway stack (hit@k, MRR)
	./evaluation/run.sh
