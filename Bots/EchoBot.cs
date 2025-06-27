using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Configuration;
using Azure;
using OpenAI.Chat; // Azure SDK preview-style ChatClient
using Azure.AI.OpenAI.Chat;
using Azure.AI.OpenAI;

namespace EchoBot.Bots
{
    public class EchoBot : ActivityHandler
    {
        private readonly IConfigurationRoot _configuration;
        private readonly AzureOpenAIClient _azureClient;
        private readonly ChatClient _chatClient;
        private readonly string _embeddingModel;
        private readonly string _searchUrl;
        private readonly string _searchKey;
        private readonly string _indexName;
        private readonly List<ChatMessage> _chatHistory;

        public EchoBot()
        {
            // Load configuration
            var builder = new ConfigurationBuilder().AddJsonFile("appsettings.json");
            _configuration = builder.Build();

            var openAiEndpoint = _configuration["OPEN_AI_ENDPOINT"];
            var openAiKey = _configuration["OPEN_AI_KEY"];
            var chatModel = _configuration["CHAT_MODEL"];
            _embeddingModel = _configuration["EMBEDDING_MODEL"];
            _searchUrl = _configuration["SEARCH_ENDPOINT"];
            _searchKey = _configuration["SEARCH_KEY"];
            _indexName = _configuration["INDEX_NAME"];

            // Initialize Azure OpenAI client and chat client
            _azureClient = new AzureOpenAIClient(new Uri(openAiEndpoint), new AzureKeyCredential(openAiKey));
            _chatClient = _azureClient.GetChatClient(chatModel);

            // Initialize chat history
            _chatHistory = new List<ChatMessage>
            {
                new SystemChatMessage("You are an HR assistant. Use candidate profiles to answer.")
            };
        }

        protected override async Task OnMessageActivityAsync(ITurnContext<IMessageActivity> turnContext, CancellationToken cancellationToken)
        {
            var userInput = turnContext.Activity.Text?.Trim();

            if (string.IsNullOrWhiteSpace(userInput))
            {
                await turnContext.SendActivityAsync(MessageFactory.Text("Please enter a valid question."), cancellationToken);
                return;
            }

            _chatHistory.Add(new UserChatMessage(userInput));

            try
            {
                #pragma warning disable AOAI001
                var options = new ChatCompletionOptions();

                options.AddDataSource(new AzureSearchChatDataSource
                {
                    Endpoint = new Uri(_searchUrl),
                    IndexName = _indexName,
                    Authentication = DataSourceAuthentication.FromApiKey(_searchKey),
                    QueryType = "vector",
                    VectorizationSource = DataSourceVectorizer.FromDeploymentName(_embeddingModel),
                });

                ChatCompletion completion = _chatClient.CompleteChat(_chatHistory, options);
                string completionText = completion.Content[0].Text;

                _chatHistory.Add(new AssistantChatMessage(completionText));
                await turnContext.SendActivityAsync(MessageFactory.Text(completionText), cancellationToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Bot Error] {ex.Message}");
                await turnContext.SendActivityAsync(MessageFactory.Text("Sorry, something went wrong while processing your request."), cancellationToken);
            }
        }
    }
}