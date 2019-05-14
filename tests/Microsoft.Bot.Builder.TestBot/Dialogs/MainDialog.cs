﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.AI.Luis;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.BotBuilderSamples.CognitiveModels;
using Microsoft.BotBuilderSamples.Dialogs;
using Microsoft.BotBuilderSamples;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Recognizers.Text.DataTypes.TimexExpression;

namespace Microsoft.BotBuilderSamples
{
    public class MainDialog : ComponentDialog
    {
        private readonly IConfiguration _configuration;
        private readonly IntentDialogMap _intentsAndDialogs;
        private readonly ILogger _logger;
        private readonly IRecognizer _luisRecognizer;

        public MainDialog(IConfiguration configuration, ILogger<MainDialog> logger, IRecognizer luisRecognizer, IntentDialogMap intentsAndDialogs)
            : base(nameof(MainDialog))
        {
            _configuration = configuration;
            _logger = logger;
            _luisRecognizer = luisRecognizer;

            AddDialog(new TextPrompt(nameof(TextPrompt)));

            // Add dialogs for intents
            _intentsAndDialogs = intentsAndDialogs;
            foreach (var dialog in intentsAndDialogs.Values)
            {
                AddDialog(dialog);
            }

            // Create and add waterfall for main conversation loop
            var steps = new WaterfallStep[]
            {
                IntroStepAsync,
                ActStepAsync,
                FinalStepAsync,
            };
            AddDialog(new WaterfallDialog(nameof(WaterfallDialog), steps));

            // The initial child Dialog to run.
            InitialDialogId = nameof(WaterfallDialog);
        }

        private async Task<DialogTurnResult> IntroStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(_configuration["LuisAppId"]) || string.IsNullOrEmpty(_configuration["LuisAPIKey"]) || string.IsNullOrEmpty(_configuration["LuisAPIHostName"]))
            {
                var activity = MessageFactory.Text("NOTE: LUIS is not configured. To enable all capabilities, add 'LuisAppId', 'LuisAPIKey' and 'LuisAPIHostName' to the appsettings.json file.");
                activity.InputHint = InputHints.IgnoringInput;
                await stepContext.Context.SendActivityAsync(activity, cancellationToken);
            }

            return await stepContext.PromptAsync(nameof(TextPrompt), new PromptOptions { Prompt = MessageFactory.Text("What can I help you with today?") }, cancellationToken);
        }

        private async Task<DialogTurnResult> ActStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            var luisResult = await _luisRecognizer.RecognizeAsync<FlightBooking>(stepContext.Context, cancellationToken);

            switch (luisResult.TopIntent().intent)
            {
                case FlightBooking.Intent.BookFlight:
                    // Call LUIS and gather any potential booking details. (Note the TurnContext has the response to the prompt.)
                    var bookingDetails = await _luisRecognizer.RecognizeAsync<BookingDetails>(stepContext.Context, cancellationToken);
 
                    // In this sample we only have a single Intent we are concerned with. However, typically a scenario
                    // will have multiple different Intents each corresponding to starting a different child Dialog.

                    // Run the BookingDialog giving it whatever details we have from the LUIS call, it will fill out the remainder.
                    return await stepContext.BeginDialogAsync(nameof(BookingDialog), bookingDetails, cancellationToken);
                case FlightBooking.Intent.GetWeather:
                    await stepContext.Context.SendActivityAsync("TODO: get weather flow here", cancellationToken: cancellationToken);
                    break;
                default:
                    await stepContext.Context.SendActivityAsync($"Sorry dave, I didn't get that (intent was {luisResult.TopIntent().intent})", cancellationToken: cancellationToken);
                    break;
            }

            return await stepContext.ReplaceDialogAsync(Id, cancellationToken: cancellationToken);
        }

        private async Task<DialogTurnResult> FinalStepAsync(WaterfallStepContext stepContext, CancellationToken cancellationToken)
        {
            // If the child dialog ("BookingDialog") was cancelled or the user failed to confirm, the Result here will be null.
            if (stepContext.Result != null)
            {
                var result = (BookingDetails)stepContext.Result;

                // Now we have all the booking details call the booking service.

                // If the call to the booking service was successful tell the user.
                var timeProperty = new TimexProperty(result.TravelDate);
                var travelDateMsg = timeProperty.ToNaturalLanguage(DateTime.Now);
                var msg = $"I have you booked to {result.Destination} from {result.Origin} on {travelDateMsg}";
                await stepContext.Context.SendActivityAsync(MessageFactory.Text(msg), cancellationToken);
            }
            else
            {
                await stepContext.Context.SendActivityAsync(MessageFactory.Text("Thank you."), cancellationToken);
            }

            return await stepContext.EndDialogAsync(cancellationToken: cancellationToken);
        }
    }
}