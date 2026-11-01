"""Each adapter module exposes build_chat(provider, model) and build_embedder(provider, model).

To add a backend (Bedrock, Vertex AI, Azure OpenAI...), add a module with those
two functions and list it here.
"""

ADAPTERS = {
    "openai_compatible": "talk_to_rag.llm.adapters.openai_compatible",
    "fake": "talk_to_rag.llm.adapters.fake",
}
