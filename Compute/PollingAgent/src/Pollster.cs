﻿// This file is part of the ArmoniK project
// 
// Copyright (C) ANEO, 2021-2021. All rights reserved.
//   W. Kirschenmann   <wkirschenmann@aneo.fr>
//   J. Gurhem         <jgurhem@aneo.fr>
//   D. Dubuc          <ddubuc@aneo.fr>
//   L. Ziane Khodja   <lzianekhodja@aneo.fr>
//   F. Lemaitre       <flemaitre@aneo.fr>
//   S. Djebbar        <sdjebbar@aneo.fr>
// 
// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU Affero General Public License as published
// by the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
// 
// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU Affero General Public License for more details.
// 
// You should have received a copy of the GNU Affero General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using ArmoniK.Api.gRPC.V1;
using ArmoniK.Core.Common;
using ArmoniK.Core.Common.Exceptions;
using ArmoniK.Core.Common.gRPC;
using ArmoniK.Core.Common.Injection.Options;
using ArmoniK.Core.Common.Storage;

using JetBrains.Annotations;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using ResultReply = ArmoniK.Api.gRPC.V1.ResultReply;
using TaskCanceledException = ArmoniK.Core.Common.Exceptions.TaskCanceledException;
using TaskStatus = ArmoniK.Api.gRPC.V1.TaskStatus;
using TimeoutException = ArmoniK.Core.Common.Exceptions.TimeoutException;

namespace ArmoniK.Core.Compute.PollingAgent;



public class Pollster
{
  private readonly IHostApplicationLifetime lifeTime_;
  private readonly ILogger<Pollster>        logger_;
  private readonly int                      messageBatchSize_;
  private readonly IQueueStorage            queueStorage_;
  private readonly PreconditionChecker      preconditionChecker_;
  private readonly DataPrefetcher           dataPrefetcher_;
  private readonly RequestProcessor         requestProcessor_;

  public Pollster(IQueueStorage            queueStorage,
                  PreconditionChecker      preconditionChecker,
                  DataPrefetcher           dataPrefetcher,
                  RequestProcessor         requestProcessor,
                  ComputePlan              options,
                  IHostApplicationLifetime lifeTime,
                  ILogger<Pollster>        logger)
  {
    if (options.MessageBatchSize < 1)
      throw new ArgumentOutOfRangeException(nameof(options),
                                            $"The minimum value for {nameof(ComputePlan.MessageBatchSize)} is 1.");

    logger_                = logger;
    queueStorage_          = queueStorage;
    lifeTime_              = lifeTime;
    preconditionChecker_   = preconditionChecker;
    dataPrefetcher_        = dataPrefetcher;
    requestProcessor_ = requestProcessor;
    messageBatchSize_      = options.MessageBatchSize;
  }

  public Task Init(CancellationToken cancellationToken)
    => queueStorage_.Init(cancellationToken);

  public async Task MainLoop(CancellationToken cancellationToken)
  {
    await Init(cancellationToken);

    cancellationToken.Register(() => logger_.LogError("Global cancellation has been triggered."));
    try
    {
      logger_.LogFunction(functionName: $"{nameof(Pollster)}.{nameof(MainLoop)}.prefetchTask.WhileLoop");
      while (!cancellationToken.IsCancellationRequested)
      {
        logger_.LogDebug("Trying to fetch messages");

        logger_.LogFunction(functionName:
                            $"{nameof(Pollster)}.{nameof(MainLoop)}.prefetchTask.WhileLoop.{nameof(queueStorage_.PullAsync)}");
        var messages = queueStorage_.PullAsync(messageBatchSize_,
                                               cancellationToken);

        await foreach (var message in messages.WithCancellation(cancellationToken))
        {
          await using var msg = message;

          using var scopedLogger = logger_.BeginNamedScope("Prefetch messageHandler",
                                                           ("messageHandler", message.MessageId),
                                                           ("task", message.TaskId));
          logger_.LogDebug("Start a new Task to process the messageHandler");

          var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(message.CancellationToken,
                                                                            cancellationToken);
          try
          {
            logger_.LogDebug("Loading task data");

            var precondition = await preconditionChecker_.CheckPreconditionsAsync(message,
                                                                                  cancellationToken);

            if (precondition is not null)
            {
              var taskData = precondition.Value.TaskData;

              await using var dispatch = precondition.Value.Dispatch;

              logger_.LogDebug("Start prefetch data");
              var computeRequestStream = await dataPrefetcher_.PrefetchDataAsync(taskData,
                                                                                 cancellationToken);


              logger_.LogDebug("Start a new Task to process the messageHandler");
              var processResult = await requestProcessor_.ProcessAsync(message,
                                                                       taskData,
                                                                       dispatch,
                                                                       computeRequestStream,
                                                                       cancellationToken);

              logger_.LogDebug("Finish task processing");


              await Task.WhenAll(processResult);

              logger_.LogDebug("Task returned");
            }
          }
          catch (Exception e)
          {
            logger_.LogWarning(e,
                               "Error with messageHandler {messageId}",
                               message.MessageId);
            throw;
          }
          finally
          {
            await msg.DisposeAsync();
          }
        }
      }

    }
    catch (Exception e)
    {
      logger_.LogCritical(e,
                          "Error in pollster");
    }

    lifeTime_.StopApplication();
  }

}