from fastapi import FastAPI
from routers import classifier, embedder

app = FastAPI()
app.include_router(classifier.router)
app.include_router(embedder.router)
