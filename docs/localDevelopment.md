# Developing AI applications locally

This sample demonstrates a local development model that uses small local models (SLMs) for generative AI scenarios. In this sample, SLMs are downloaded and used in two different ways:
* Using [Ollama](https://ollama.com/), running in a Docker container, to download and access the models.
* Accessing a REST endpoint hosted in a Python project, which downloads and accesses SLMs using the [transformers](https://pypi.org/project/transformers/) library.

## Using Ollama to access SLMs

that downloads the models directlyand run them in a dock container. If you look at the app host project for this program, you'll find the calls to work with Ollama and there's a file called Ollama resource extraction.ca. That contains the logic used to create the dark container, which will run Ollama and includes the commands that will download the models we use with this sample.This sample demonstrates a local development model for working with AI. This is done using the Ollama project to download models locally and run them in a dock container. If you look at the app host project for this program, you'll find the calls to work with Ollama and there's a file called Ollama resource extraction.ca. That contains the logic use to create the dark container, which will run Ollama and includes the commands that will download the models we use with this sample.

## Using Python to access SLMs

When building your applications, there may be models that either you can't access through Ollama or other .NET libraries; or you may be working with another team that chose to use Python to build a backend API that uses AI. This sample demonstrates this scenario in the PythonInference project:

* [In the PythonInference/routers/classifier.py file](https://github.com/dotnet/eShopSupport/blob/main/src/PythonInference/routers/classifier.py#L6) The [transformers](https://pypi.org/project/transformers/) library is used to classify (assign labels to) content using the [cross-encoder/nli-MiniLM2-L6-H768](https://huggingface.co/cross-encoder/nli-MiniLM2-L6-H768) model
* An example of calling this API can be found in the [Backend/API/TicketAPI.cs file, CreateTicketAsync() method](https://github.com/dotnet/eShopSupport/blob/main/src/Backend/Api/TicketApi.cs#L176), which passes message content to classify and a set of labels to use for classification.
