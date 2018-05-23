﻿using IntermediatorBotSample.Resources;
using Microsoft.Bot.Schema;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Underscore.Bot.MessageRouting;
using Underscore.Bot.MessageRouting.DataStore;
using Underscore.Bot.MessageRouting.Models;
using Underscore.Bot.MessageRouting.Results;

namespace IntermediatorBotSample.MessageRouting
{
    /// <summary>
    /// Contains utility methods for accepting and rejecting connection requests.
    /// </summary>
    public class ConnectionRequestHandler
    {
        private const string ChannelIdEmulator = "emulator";
        private const string ChannelIdFacebook = "facebook";
        private const string ChannelIdSkype = "skype";

        /// <summary>
        /// Do not try to create direct conversations when the owner is on one of these channels
        /// </summary>
        private readonly IList<string> NoDirectConversationsWithChannels = new List<string>()
        {
            ChannelIdEmulator,
            ChannelIdFacebook,
            ChannelIdSkype
        };

        /// <summary>
        /// Tries to accept/reject a pending connection request.
        /// </summary>
        /// <param name="messageRouter">The message router.</param>
        /// <param name="messageRouterResultHandler">The message router result handler.</param>
        /// <param name="sender">The sender party (accepter/rejecter).</param>
        /// <param name="doAccept">If true, will try to accept the request. If false, will reject.</param>
        /// <param name="channelAccountIdOfPartyToAcceptOrReject">The channel account ID of the party whose request to accep/reject.</param>
        /// <returns>The result.</returns>
        public async Task<AbstractMessageRouterResult> AcceptOrRejectRequestAsync(
            MessageRouter messageRouter, MessageRouterResultHandler messageRouterResultHandler,
            ConversationReference sender, bool doAccept, string channelAccountIdOfPartyToAcceptOrReject)
        {
            AbstractMessageRouterResult messageRouterResult = new ConnectionRequestResult()
            {
                Type = ConnectionRequestResultType.Error
            };

            RoutingDataManager routingDataManager = messageRouter.RoutingDataManager;
            ConnectionRequest connectionRequest = null;

            if (routingDataManager.GetConnectionRequests().Count > 0)
            {
                try
                {
                    // Find the connection request based on the channel account ID of the requestor
                    connectionRequest = routingDataManager.GetConnectionRequests().Single(request =>
                            (RoutingDataManager.GetChannelAccount(request.Requestor, out bool isBot) != null
                              && RoutingDataManager.GetChannelAccount(request.Requestor, out isBot).Id
                                .Equals(channelAccountIdOfPartyToAcceptOrReject)));
                }
                catch (InvalidOperationException e)
                {
                    messageRouterResult.ErrorMessage = string.Format(
                        Strings.FailedToFindPendingRequestForUserWithErrorMessage,
                        channelAccountIdOfPartyToAcceptOrReject,
                        e.Message);
                }
            }

            if (connectionRequest != null)
            {
                Connection connection = null;

                if (sender != null)
                {
                    connection = routingDataManager.FindConnection(sender);
                }

                ConversationReference senderInConnection = null;
                ConversationReference counterpart = null;

                if (connection != null && connection.ConversationReference1 != null)
                {
                    if (RoutingDataManager.HaveMatchingChannelAccounts(sender, connection.ConversationReference1))
                    {
                        senderInConnection = connection.ConversationReference1;
                        counterpart = connection.ConversationReference2;
                    }
                    else
                    {
                        senderInConnection = connection.ConversationReference2;
                        counterpart = connection.ConversationReference1;
                    }
                }

                if (doAccept)
                {
                    if (senderInConnection != null)
                    {
                        // The sender (accepter/rejecter) is ALREADY connected to another party
                        if (counterpart != null)
                        {
                            messageRouterResult.ErrorMessage = string.Format(
                                Strings.AlreadyConnectedWithUser,
                                RoutingDataManager.GetChannelAccount(counterpart, out bool isBot)?.Name);
                        }
                        else
                        {
                            messageRouterResult.ErrorMessage = Strings.ErrorOccured;
                        }
                    }
                    else
                    {
                        bool createNewDirectConversation =
                            !(NoDirectConversationsWithChannels.Contains(sender.ChannelId.ToLower()));

                        // Try to accept
                        messageRouterResult = await messageRouter.ConnectAsync(
                            sender,
                            connectionRequest.Requestor,
                            createNewDirectConversation);
                    }
                }
                else
                {
                    // Note: Rejecting is OK even if the sender is alreay connected
                    messageRouterResult = messageRouter.RejectConnectionRequest(connectionRequest.Requestor, sender);
                }
            }
            else
            {
                messageRouterResult.ErrorMessage = Strings.FailedToFindPendingRequest;
            }

            return messageRouterResult;
        }

        /// <summary>
        /// Tries to reject all pending requests.
        /// </summary>
        /// <param name="messageRouter">The message router.</param>
        /// <param name="messageRouterResultHandler">The message router result handler.</param>
        /// <returns>True, if successful. False otherwise.</returns>
        public async Task<bool> RejectAllPendingRequestsAsync(
            MessageRouter messageRouter, MessageRouterResultHandler messageRouterResultHandler)
        {
            bool wasSuccessful = false;
            IList<ConnectionRequest> connectionRequests = messageRouter.RoutingDataManager.GetConnectionRequests();

            if (connectionRequests.Count > 0)
            {
                IList<ConnectionRequestResult> connectionRequestResults =
                    new List<ConnectionRequestResult>();

                foreach (ConnectionRequest connectionRequest in connectionRequests)
                {
                    connectionRequestResults.Add(
                        messageRouter.RejectConnectionRequest(connectionRequest.Requestor));
                }

                foreach (ConnectionRequestResult connectionRequestResult in connectionRequestResults)
                {
                    await messageRouterResultHandler.HandleResultAsync(connectionRequestResult);
                }

                wasSuccessful = true;
            }

            return wasSuccessful;
        }
    }
}
