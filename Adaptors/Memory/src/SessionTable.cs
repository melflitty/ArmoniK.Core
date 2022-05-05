﻿// This file is part of the ArmoniK project
// 
// Copyright (C) ANEO, 2021-2022. All rights reserved.
//   W. Kirschenmann   <wkirschenmann@aneo.fr>
//   J. Gurhem         <jgurhem@aneo.fr>
//   D. Dubuc          <ddubuc@aneo.fr>
//   L. Ziane Khodja   <lzianekhodja@aneo.fr>
//   F. Lemaitre       <flemaitre@aneo.fr>
//   S. Djebbar        <sdjebbar@aneo.fr>
//   J. Fonseca        <jfonseca@aneo.fr>
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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using ArmoniK.Core.Common;
using ArmoniK.Core.Common.Exceptions;
using ArmoniK.Core.Common.Storage;

using Microsoft.Extensions.Logging;

using TaskOptions = ArmoniK.Api.gRPC.V1.TaskOptions;

namespace ArmoniK.Core.Adapters.Memory;

public class SessionTable : ISessionTable
{
  private readonly ConcurrentDictionary<string, SessionData> storage_;

  public SessionTable(ConcurrentDictionary<string, SessionData>                          storage,
                      ILogger<SessionTable>                                              logger)
  {
    storage_            = storage;
    Logger              = logger;
  }

  /// <inheritdoc />
  public ValueTask<bool> Check(HealthCheckTag tag)
    => ValueTask.FromResult(true);

  /// <inheritdoc />
  public Task Init(CancellationToken cancellationToken)
    => Task.CompletedTask;

  /// <inheritdoc />
  public Task CreateSessionDataAsync(string            rootSessionId,
                                     string            parentTaskId,
                                     TaskOptions       defaultOptions,
                                     CancellationToken cancellationToken = default)
  {
    storage_.TryAdd(rootSessionId,
                    new SessionData(rootSessionId,
                                    "Running",
                                    defaultOptions));
    return Task.CompletedTask;
  }

  /// <inheritdoc />
  public Task<bool> IsSessionCancelledAsync(string            sessionId,
                                            CancellationToken cancellationToken = default)
  {
    if (!storage_.ContainsKey(sessionId))
    {
      throw new SessionNotFoundException($"Key '{sessionId}' not found");
    }

    return Task.FromResult(storage_[sessionId]
                             .Status == "Cancelled");
  }

  /// <inheritdoc />
  public Task<TaskOptions> GetDefaultTaskOptionAsync(string            sessionId,
                                                     CancellationToken cancellationToken = default)
  {
    if (!storage_.ContainsKey(sessionId))
    {
      throw new SessionNotFoundException($"Key '{sessionId}' not found");
    }

    return Task.FromResult(storage_[sessionId]
                             .Options);
  }

  /// <inheritdoc />
  public Task CancelSessionAsync(string            sessionId,
                                       CancellationToken cancellationToken = default)
  {
    storage_.AddOrUpdate(sessionId,
                         _ => throw new SessionNotFoundException($"Key '{sessionId}' not found"),
                         (_,
                          data) =>
                         {
                           if (data.Status == "Cancelled")
                           {
                             throw new ArmoniKException("Session already cancelled");
                           }
                           return data with
                                  {
                                    Status = "Cancelled",
                                  };
                         });
    return Task.CompletedTask;
  }


  /// <inheritdoc />
  public Task DeleteSessionAsync(string            sessionId,
                                       CancellationToken cancellationToken = default)
  {
    if (!storage_.ContainsKey(sessionId))
    {
      throw new SessionNotFoundException($"No session with id '{sessionId}' found");
    }

    storage_.Remove(sessionId, out _);
    return Task.CompletedTask;
  }


  /// <inheritdoc />
  public IAsyncEnumerable<string> ListSessionsAsync(CancellationToken cancellationToken = default)
    => storage_.Keys.ToAsyncEnumerable();

  /// <inheritdoc />
  public ILogger Logger { get; }
}
