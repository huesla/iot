﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

// Disable these StyleCop rules for this file, as we are using native names here.
#pragma warning disable SA1300 // Element should begin with upper-case letter

namespace System.Device.Gpio.Interop.Unix.libgpiod.v2.ValueTypes;

/// <summary>
/// Value type for GPIO edge event sequence number
/// </summary>
public readonly record struct SequenceNumber
{
    private readonly ulong _value;

    /// <summary>
    /// Creates a sequence number with value 0.
    /// </summary>
    public SequenceNumber()
    {
        _value = 0;
    }

    private SequenceNumber(ulong val)
    {
        _value = val;
    }

    /// <summary>
    /// Implicit cast operator SequenceNumber -> ulong
    /// </summary>
    /// <param name="s">The sequenceNumber</param>
    public static implicit operator ulong(SequenceNumber s)
    {
        return s._value;
    }

    /// <summary>
    /// Implicit cast operator ulong -> SequenceNumber
    /// </summary>
    /// <param name="val">The ulong value</param>
    public static implicit operator SequenceNumber(ulong val)
    {
        return new SequenceNumber(val);
    }

    /// <summary>
    /// Returns ulong value as string
    /// </summary>
    public override string ToString()
    {
        return _value.ToString();
    }
}
