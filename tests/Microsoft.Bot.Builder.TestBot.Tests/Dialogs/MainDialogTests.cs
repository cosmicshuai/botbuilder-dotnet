﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.BotBuilderSamples.CognitiveModels;
using Microsoft.BotBuilderSamples.Dialogs;
using Microsoft.BotBuilderSamples.Tests.Utils;
using Microsoft.BotBuilderSamples.Tests.Utils.XUnit;
using Microsoft.Extensions.Configuration;
using Moq;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.BotBuilderSamples.Tests.Dialogs
{
    public class MainDialogTests : DialogTestsBase
    {
        private readonly IntentDialogMap _intentsAndDialogs;
        private readonly Mock<IRecognizer> _mockLuisRecognizer;

        public MainDialogTests(ITestOutputHelper output)
            : base(output)
        {
            _mockLuisRecognizer = new Mock<IRecognizer>();
            _intentsAndDialogs = new IntentDialogMap
            {
                { FlightBooking.Intent.BookFlight, DialogUtils.CreateMockDialog<BookingDialog>().Object },
                { FlightBooking.Intent.GetWeather, DialogUtils.CreateMockDialog<Dialog>(null, "GetWeatherDialog").Object },
            };
        }

        public static MainDialogData<BookingDetails, string> MainDialogDataSource =>
            new MainDialogData<BookingDetails, string>
            {
                { null, "Thank you." },
                {
                    new BookingDetails
                    {
                        Destination = "Bahamas",
                        Origin = "New York",
                        TravelDate = $"{DateTime.UtcNow.AddDays(1):yyyy-MM-dd}",
                    },
                    "I have you booked to Bahamas from New York on tomorrow"
                },
                {
                    new BookingDetails
                    {
                        Destination = "Seattle",
                        Origin = "Bahamas",
                        TravelDate = $"{DateTime.UtcNow:yyyy-MM-dd}",
                    },
                    "I have you booked to Seattle from Bahamas on today"
                },
            };

        [Fact]
        public void DialogConstructor()
        {
            // TODO: check with the team if there's value in these types of test or if there's a better way of asserting the
            // dialog got composed properly.
            var sut = new MainDialog(MockConfig.Object, MockLogger.Object, _mockLuisRecognizer.Object, _intentsAndDialogs);

            Assert.Equal("MainDialog", sut.Id);
            Assert.IsType<TextPrompt>(sut.FindDialog("TextPrompt"));
            Assert.NotNull(sut.FindDialog("BookingDialog"));
            Assert.IsType<WaterfallDialog>(sut.FindDialog("WaterfallDialog"));
        }

        [Theory]
        [InlineData("", "", "")]
        [InlineData("A", "", "")]
        [InlineData("", "B", "")]
        [InlineData("", "", "C")]
        [InlineData("A", "B", "")]
        [InlineData("A", "", "C")]
        [InlineData("", "B", "C")]
        public async Task ShowsMessageIfLuisNotConfigured(string luisAppId, string luisApiKey, string luisApiHostName)
        {
            // Arrange
            var luisMockConfig = new Mock<IConfiguration>();
            luisMockConfig.Setup(x => x["LuisAppId"]).Returns(luisAppId);
            luisMockConfig.Setup(x => x["LuisAPIKey"]).Returns(luisApiKey);
            luisMockConfig.Setup(x => x["LuisAPIHostName"]).Returns(luisApiHostName);

            var sut = new MainDialog(luisMockConfig.Object, MockLogger.Object, _mockLuisRecognizer.Object, _intentsAndDialogs);
            var testBot = new DialogsTestBot(sut, Output);

            // Act/Assert
            var reply = await testBot.SendAsync<IMessageActivity>("hi");
            Assert.Equal("NOTE: LUIS is not configured. To enable all capabilities, add 'LuisAppId', 'LuisAPIKey' and 'LuisAPIHostName' to the appsettings.json file.", reply.Text);

            reply = await testBot.GetNextReplyAsync<IMessageActivity>();
            Assert.Equal("What can I help you with today?", reply.Text);
        }

        [Fact]
        public async Task ShowsPromptIfLuisIsConfigured()
        {
            // Arrange
            var sut = new MainDialog(MockConfig.Object, MockLogger.Object, _mockLuisRecognizer.Object, _intentsAndDialogs);
            var testBot = new DialogsTestBot(sut, Output);

            // Act/Assert
            var reply = await testBot.SendAsync<IMessageActivity>("hi");
            Assert.Equal("What can I help you with today?", reply.Text);
        }

        [Theory]
        [InlineData("I want to book a flight", "BookFlight", "BookingDialog mock invoked")]
        [InlineData("What's the weather like?", "GetWeather", "TODO: get weather flow here")]
        [InlineData("bananas", "None", "Sorry Dave, I didn't get that (intent was None)")]
        public async Task TaskSelector(string utterance, string intent, string invokedDialog)
        {
            _mockLuisRecognizer.SetupRecognizeAsync<FlightBooking>(
                new FlightBooking
                {
                    Intents = new Dictionary<FlightBooking.Intent, IntentScore>
                    {
                        { Enum.Parse<FlightBooking.Intent>(intent), new IntentScore() { Score = 1 } },
                    },
                });

            _mockLuisRecognizer.SetupRecognizeAsync<BookingDetails>(
                new BookingDetails
                {
                    Origin = "irrelevant",
                    Destination = "irrelevant",
                    TravelDate = "irrelevant",
                });


            var sut = new MainDialog(MockConfig.Object, MockLogger.Object, _mockLuisRecognizer.Object, _intentsAndDialogs);
            var testBot = new DialogsTestBot(sut, Output);

            var reply = await testBot.SendAsync<IMessageActivity>("irrelevant");
            Assert.Equal("What can I help you with today?", reply.Text);
            reply = await testBot.SendAsync<IMessageActivity>(utterance);
            Assert.Equal(invokedDialog, reply.Text);
            reply = await testBot.GetNextReplyAsync<IMessageActivity>();
            Assert.Equal("What else can I do for you?", reply.Text);
        }

        [Theory]
        [MemberData(nameof(MainDialogDataSource))]
        public async Task TaskSelectorWithMemberData(BookingDetails expectedResult, string endMessage)
        {
            _mockLuisRecognizer.SetupRecognizeAsync(
                new FlightBooking
                {
                    Intents = new Dictionary<FlightBooking.Intent, IntentScore>
                    {
                        { FlightBooking.Intent.BookFlight, new IntentScore { Score = 1 } },
                    },
                });

            _mockLuisRecognizer.SetupRecognizeAsync(
                new BookingDetails
                {
                    Origin = "irrelevant",
                    Destination = "irrelevant",
                    TravelDate = "irrelevant",
                });

            // Arrange
            var sut = new MainDialog(MockConfig.Object, MockLogger.Object, _mockLuisRecognizer.Object, _intentsAndDialogs);
            var testBot = new DialogsTestBot(sut, Output);

            // Act/Assert
            var reply = await testBot.SendAsync<IMessageActivity>("Hi");
            Assert.Equal("What can I help you with today?", reply.Text);

            reply = await testBot.SendAsync<IMessageActivity>("irrelevant");
            Assert.Equal("BookingDialog mock invoked", reply.Text);

            reply = await testBot.GetNextReplyAsync<IMessageActivity>();
            Assert.Equal(endMessage, reply.Text);
        }

        public class MainDialogData<TBookingDetails, TExpectedReply> : TheoryData
            where TBookingDetails : BookingDetails
        {
            public void Add(TBookingDetails bookingDetails, TExpectedReply expectedReply)
            {
                AddRow(bookingDetails, expectedReply);
            }
        }
    }
}
