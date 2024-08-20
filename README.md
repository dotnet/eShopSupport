# eShopSupport 

A sample .NET application showcasing common use cases and development practices for build AI solutions in .NET (Generative AI, specifically). This sample demonstrates a customer support application for an e-commerce website using a services-based architecture with .NET Aspire. It includes support for the following AI use cases:

* Text classification, applying labels based on content
* Sentiment analysis based on message content
* Summarization of large sets of text
* Synthetic data generation, creating test content for the sample
* Chat bot interactions with chat history and suggested responses

<img width=400 align=top src=https://github.com/user-attachments/assets/5a41493f-565b-4dd0-ae31-1b5c3c2f6d22>

<img width=400 align=top src=https://github.com/user-attachments/assets/7930a940-bb31-4dc0-b5f6-738d43dfcfe5>

This sample also demonstrates the following development practices:

* Developing a solution locally, using small local models
* Evaluating the quality of AI responses using grounded Q&A data
* Leveraging Python projects as part of a .NET Aspire solution
* Deploying the application, including small local models, to the Cloud (coming soon)

## Architecture

![image](https://github.com/user-attachments/assets/3c339d0d-507a-416b-94ba-0e179d6ff2f5)

## Getting Started

### Prerequisites

- Clone the eShopSupport repository: https://github.com/dotnet/eshopsupport
- [Install & start Docker Desktop](https://docs.docker.com/engine/install/)

#### Windows with Visual Studio
- Install [Visual Studio 2022 version 17.10 or newer](https://visualstudio.microsoft.com/vs/).
  - Select the following workloads:
    - `ASP.NET and web development` workload.
    - `.NET Aspire SDK` component in `Individual components`.

#### Mac, Linux, & Windows without Visual Studio
- Install the latest [.NET 8 SDK](https://dot.net/download?cid=eshop)
- Install the [.NET Aspire workload](https://learn.microsoft.com/dotnet/aspire/fundamentals/setup-tooling?tabs=dotnet-cli%2Cunix#install-net-aspire) with the following commands:

  ```powershell
  dotnet workload update
  dotnet workload install aspire
  dotnet restore eShopSupport.sln
  ```
- (Optionally) Install [Visual Studio Code](https://code.visualstudio.com) with the [C# Dev Kit extension](https://marketplace.visualstudio.com/items?itemName=ms-dotnettools.csdevkit)

### Running the solution

> [!WARNING]
> Remember to ensure that Docker is started

* (Windows only) Run the application from Visual Studio:
  - Open the `eShopSupport.sln` file in Visual Studio
  - Ensure that `AppHost` is your startup project
  - Hit Ctrl-F5 to launch .NET Aspire

* Or run the application from your terminal:

  ```powershell
  dotnet run --project src/AppHost
  ```

  then look for lines like this in the console output in order to find the URL to open the Aspire dashboard:

  ```sh
  Login to the dashboard at: http://localhost:17191/login?t=uniquelogincodeforyou
  ```

> You may need to install ASP.NET Core HTTPS development certificates first, and then close all browser tabs. Learn more at https://aka.ms/aspnet/https-trust-dev-cert

# Contributing

This project has adopted the [Microsoft Open Source Code of Conduct](https://opensource.microsoft.com/codeofconduct/). For more information see the [Code of Conduct FAQ](https://opensource.microsoft.com/codeofconduct/faq/) or contact [opencode@microsoft.com](mailto:opencode@microsoft.com) with any additional questions or comments.

# Sample data

The sample data is defined in [seeddata](https://github.com/dotnet/eShopSupport/tree/main/seeddata). All products/descriptions/brands, manuals, customers, and support tickets names are fictional and were generated using [GPT-35-Turbo](https://learn.microsoft.com/en-us/azure/ai-services/openai/how-to/chatgpt) using the included [DataGenerator](https://github.com/dotnet/eShopSupport/tree/main/seeddata/DataGenerator) project.
