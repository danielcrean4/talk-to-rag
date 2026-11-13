"""Score retrieval against the running API with a golden question set.

    uv run python -m talk_to_rag.evaluation
    uv run python -m talk_to_rag.evaluation --email you@example.com --password ...
    uv run python -m talk_to_rag.evaluation --token $JWT --mode semantic --expand

Each question names the source title (file name without extension) and where the answer
is: `target_pages`, `target_text` (phrases the passage must contain, for sources without
pages), or both. The default set is about the documents in `sample/`, so run `make sample`
first. Search is limited to the sources the questions name, so other documents in the
library do not change the score.
"""

import argparse
import json
import os
import sys
import urllib.error
import urllib.request
from pathlib import Path

from talk_to_rag.evaluation.metrics import KS, Hit, Target, below, first_relevant_rank, summarize

DATASET_PATH = Path("evaluation") / "sample_questions.json"


def _default_dataset() -> Path | None:
    """The sample question set, when this runs from a checkout of the repository."""
    candidates = (parent / DATASET_PATH for parent in Path(__file__).resolve().parents)
    return next((path for path in candidates if path.is_file()), None)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    dataset = _default_dataset()
    parser.add_argument("--dataset", type=Path, default=dataset, required=dataset is None)
    parser.add_argument("--api-url", default="http://127.0.0.1:5000")
    parser.add_argument("--token", default=os.environ.get("TALK_TO_RAG_TOKEN"))
    parser.add_argument("--email")
    parser.add_argument("--password")
    parser.add_argument("--mode", choices=["hybrid", "semantic", "keyword"], default="hybrid")
    parser.add_argument("--expand", action="store_true", help="turn on query expansion")
    parser.add_argument("--limit", type=int, default=max(KS))
    parser.add_argument("--minimums", type=Path, help="JSON file of lowest accepted scores per mode")
    args = parser.parse_args()

    questions = json.loads(args.dataset.read_text(encoding="utf-8"))
    try:
        token = args.token or _login(args.api_url, args.email, args.password)
        source_ids = _source_ids(args.api_url, token, {q["target_source_title"] for q in questions})
        results = []
        for q in questions:
            hits = _search(args.api_url, token, q["question"], args.mode, args.expand, args.limit, source_ids)
            target = Target(
                q["target_source_title"],
                frozenset(q.get("target_pages", [])),
                tuple(q.get("target_text", [])),
            )
            results.append((hits, target))
            print(f"{q['id']:<14} rank {first_relevant_rank(hits, target) or '-':>2}  {q['question']}")
    except (urllib.error.URLError, KeyError, ValueError) as error:
        sys.exit(f"error: {error}")

    metrics = summarize(results)
    print(f"\n{len(questions)} questions, mode={args.mode}, expand={args.expand}")
    for name, value in metrics.items():
        print(f"{name:<7} {value:.3f}")

    if args.minimums and (low := below(metrics, json.loads(args.minimums.read_text(encoding="utf-8")).get(args.mode, {}))):
        sys.exit(f"error: below the minimum for {args.mode}: {', '.join(low)}")


def _login(api_url: str, email: str | None, password: str | None) -> str:
    # Without accounts (the default), the API hands out the local user's session.
    with urllib.request.urlopen(api_url + "/api/auth/session") as response:
        session = json.load(response)
    if session["singleUser"]:
        return session["session"]["accessToken"]
    if not (email and password):
        raise ValueError("pass --token (or set TALK_TO_RAG_TOKEN), or --email and --password")
    return _request(api_url + "/api/auth/login", {"email": email, "password": password})["accessToken"]


def _source_ids(api_url: str, token: str, titles: set[str]) -> list[str]:
    wanted = {title.casefold() for title in titles}
    sources = _request(api_url + "/api/sources", token=token)
    found = {s["title"].casefold(): s["id"] for s in sources if s["title"].casefold() in wanted and s["status"] == "Ready"}
    if missing := sorted(wanted - found.keys()):
        raise ValueError(f"not loaded or not ready: {', '.join(missing)}")
    return list(found.values())


def _search(api_url: str, token: str, query: str, mode: str, expand: bool, limit: int, source_ids: list[str]) -> list[Hit]:
    body = {"query": query, "mode": mode, "expand": expand, "limit": limit, "sourceIds": source_ids}
    response = _request(api_url + "/api/search", body, token)
    # The search response carries snippets; the saved conversation has each passage in full.
    conversation = _request(f"{api_url}/api/conversations/{response['conversationId']}", token=token)
    content = {c["number"]: c["content"] for c in conversation["messages"][-1]["citations"]}
    passages = [
        (passage["number"], group["source"]["title"], passage["pageNumber"])
        for group in response["groups"]
        for passage in group["passages"]
    ]
    # Groups are by source; "number" is the global rank across all of them.
    return [Hit(title, page, content.get(number, "")) for number, title, page in sorted(passages)]


def _request(url: str, body: dict | None = None, token: str | None = None) -> dict | list:
    headers = {"Content-Type": "application/json"}
    if token:
        headers["Authorization"] = f"Bearer {token}"
    data = json.dumps(body).encode() if body is not None else None
    request = urllib.request.Request(url, data, headers, method="POST" if body is not None else "GET")
    with urllib.request.urlopen(request) as response:
        return json.load(response)


if __name__ == "__main__":
    main()
