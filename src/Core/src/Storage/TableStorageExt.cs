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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using ArmoniK.Core.gRPC.V1;

using JetBrains.Annotations;

using TaskStatus = ArmoniK.Core.gRPC.V1.TaskStatus;

namespace ArmoniK.Core.Storage
{
  [PublicAPI]
  public static class TableStorageExt
  {
    public static async Task<bool> IsTaskCompleted(this ITableStorage tableStorage,
                                                   TaskData           taskData,
                                                   CancellationToken  cancellationToken = default)
    {
      var status = taskData.Status;
      if (status != TaskStatus.Completed)
        return false;

      if (taskData.Dependencies.Count == 0)
        return true;

      var cts = new CancellationTokenSource();
      var aggregateCts = CancellationTokenSource.CreateLinkedTokenSource(cts.Token,
                                                                         cancellationToken);

      var futureDependenciesData = taskData.Dependencies.Select(async id =>
                                                                        {
                                                                          var depTaskData = await tableStorage.ReadTaskAsync(new(taskData.Id){Task = id},
                                                                                                                             aggregateCts.Token);
                                                                          return await tableStorage.IsTaskCompleted(depTaskData,
                                                                                                                    aggregateCts.Token);
                                                                        }).ToList(); // ToListAsync ensures that all operations have started before processing results

      while (futureDependenciesData.Count > 0)
      {
        var finished = await Task.WhenAny(futureDependenciesData);
        futureDependenciesData.Remove(finished);

        if (finished.Result)
          continue;

        cts.Cancel();
        try
        {
          await Task.WhenAll(futureDependenciesData); // avoid dandling running Tasks
        }
        catch (OperationCanceledException)
        {
        }

        return false;
      }

      return true;
    }

    public static Task CancelTask(this ITableStorage tableStorage, TaskId id, CancellationToken cancellationToken = default)
      => tableStorage.UpdateTaskStatusAsync(id,
                                            TaskStatus.Canceling,
                                            cancellationToken);

    public static Task<int> CancelTask(this ITableStorage tableStorage,
                                       TaskFilter         filter,
                                       CancellationToken  cancellationToken = default)
      => tableStorage.UpdateTaskStatusAsync(filter,
                                            TaskStatus.Canceling,
                                            cancellationToken);


    public static Task FinalizeTaskCreation(this ITableStorage tableStorage,
                                            TaskId             taskId,
                                            CancellationToken  cancellationToken = default)
      => tableStorage.UpdateTaskStatusAsync(taskId,
                                            TaskStatus.Submitted,
                                            cancellationToken);

    public static Task FinalizeTaskCreation(this ITableStorage tableStorage,
                                            TaskFilter         filter,
                                            CancellationToken  cancellationToken = default)
      => tableStorage.UpdateTaskStatusAsync(filter,
                                            TaskStatus.Submitted,
                                            cancellationToken);
  }
}
