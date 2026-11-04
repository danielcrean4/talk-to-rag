.PHONY: up down logs ps migrate test test-ai test-api health

up:  ## Build and start Postgres, the AI service and the API
	docker compose up -d --build --wait

down:  ## Stop the stack (data volumes are kept)
	docker compose down

logs:
	docker compose logs -f

ps:
	docker compose ps

migrate:  ## Apply EF Core migrations to the compose Postgres
	cd services/api && dotnet tool restore && dotnet ef database update \
		--project src/TalkToRag.Infrastructure --startup-project src/TalkToRag.Api

test: test-ai test-api test-eval

test-ai:
	cd services/ai && uv run pytest -q

test-api:
	cd services/api && dotnet test

test-eval:
	python3 evaluation/evaluate.py --mock

health:  ## Ask each service if it's up
	@curl -fsS http://localhost:8000/health && echo
	@curl -fsS http://localhost:5000/api/health && echo
