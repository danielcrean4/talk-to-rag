"""Request and response bodies: the HTTP contract the .NET API codes against."""

from typing import Literal

from pydantic import BaseModel


class HealthResponse(BaseModel):
    status: Literal["ok"]
    service: str
    version: str
