from fastapi import APIRouter
from pydantic import BaseModel
from sentence_transformers import SentenceTransformer

router = APIRouter()
model = SentenceTransformer('sentence-transformers/all-MiniLM-L6-v2')

class EmbedRequest(BaseModel):
    sentences: list[str]

@router.post("/embed")
def embed_sentences(req: EmbedRequest) -> list[list[float]]:
    embeddings = model.encode(req.sentences)
    return embeddings.tolist()
