using System;

namespace Norcom.Launcher.Core.Services;

public readonly record struct TransferSnapshot(string Label, double Fraction, long Received, long? Total, double BytesPerSecond, TimeSpan? Eta, long Epoch = 0);
