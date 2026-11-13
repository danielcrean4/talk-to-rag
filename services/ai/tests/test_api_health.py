from fastapi.testclient import TestClient

from talk_to_rag.api.app import create_app


def test_health_reports_ok():
    response = TestClient(create_app()).get("/health")

    assert response.status_code == 200
    body = response.json()
    assert body["status"] == "ok"
    assert body["service"] == "talk-to-rag-ai"
    assert body["version"]


def test_health_is_in_the_openapi_contract():
    paths = TestClient(create_app()).get("/openapi.json").json()["paths"]

    assert "/health" in paths
