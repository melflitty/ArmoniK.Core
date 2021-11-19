﻿// This file is part of ArmoniK project.
// 
// Copyright (c) ANEO. All rights reserved.
//   W. Kirschenmann <wkirschenmann@aneo.fr>

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace ArmoniK.Core.Storage
{
  public interface IObjectStorage
  {
    Task<byte[]> GetOrAddAsync(string key, byte[] value, CancellationToken cancellationToken = default);

    Task AddOrUpdateAsync(string key, byte[] value, CancellationToken cancellationToken = default);

    Task<byte[]> TryGetValuesAsync(string key, CancellationToken cancellationToken = default);

    Task<bool> TryDeleteAsync(string key, CancellationToken cancellationToken = default);
  }
}
