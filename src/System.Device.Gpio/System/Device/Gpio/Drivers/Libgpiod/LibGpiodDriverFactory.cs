﻿// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;
using System.Device.Gpio.Drivers.Libgpiod.V1;
using System.Device.Gpio.Drivers.Libgpiod.V2;
using System.Device.Gpio.Libgpiod.V2;
using System.IO;
using System.Linq;
using System.Threading;

namespace System.Device.Gpio.Drivers;

/// <summary>
/// Driver factory for different versions of libgpiod.
/// </summary>
internal sealed class LibGpiodDriverFactory
{
    private const string DriverVersionEnvVar = "DOTNET_IOT_LIBGPIOD_DRIVER_VERSION";
    private const string LibrarySearchPattern = "libgpiod.so*";

    private static readonly Dictionary<string, LibGpiodDriverVersion> _libraryToDriverVersionMap = new()
    {
        { "libgpiod.so.0", LibGpiodDriverVersion.V1 },
        { "libgpiod.so.1", LibGpiodDriverVersion.V1 },
        { "libgpiod.so.2", LibGpiodDriverVersion.V1 },
        { "libgpiod.so.3", LibGpiodDriverVersion.V2 }
    };

    private static readonly Dictionary<LibGpiodDriverVersion, string[]> _driverVersionToLibrariesMap = new()
    {
        { LibGpiodDriverVersion.V1, new[] { "libgpiod.so.0", "libgpiod.so.1", "libgpiod.so.2" } },
        { LibGpiodDriverVersion.V2, new[] { "libgpiod.so.3" } }
    };

    private static readonly string[] _librarySearchPaths = { "/lib", "/usr/lib", "/usr/local/lib" }; // Based on Linux FHS standard

    /// <summary>
    /// The value set by DOTNET_IOT_LIBGPIOD_DRIVER_VERSION. Null when not set.
    /// </summary>
    private string? DriverVersionEnvVarValue => _driverVersionEnvVarValue.Value;
    private readonly Lazy<string?> _driverVersionEnvVarValue;

    /// <summary>
    /// The driver version that was resolved based on the value of DOTNET_IOT_LIBGPIOD_DRIVER_VERSION. Null when env var not set.
    /// </summary>
    private LibGpiodDriverVersion? DriverVersionSetByEnvVar => _driverVersionSetByEnvVar.Value;
    private readonly Lazy<LibGpiodDriverVersion?> _driverVersionSetByEnvVar;

    /// <remarks>
    /// Driver version that is picked by this factory when no version is explicitly specified. Null when libpgiod is not installed.
    /// </remarks>
    private LibGpiodDriverVersion? AutomaticallySelectedDriverVersion => _automaticallySelectedDriverVersion.Value;
    private readonly Lazy<LibGpiodDriverVersion?> _automaticallySelectedDriverVersion;

    private readonly Lazy<LibGpiodDriverVersion[]> _driverCandidates;

    /// <summary>
    /// Collection of installed libgpiod libraries (their file name).
    /// </summary>
    private IEnumerable<string> InstalledLibraries => _installedLibraries.Value;
    private readonly Lazy<IEnumerable<string>> _installedLibraries;

    private LibGpiodDriverFactory()
    {
        _installedLibraries = new Lazy<IEnumerable<string>>(GetInstalledLibraries, LazyThreadSafetyMode.PublicationOnly);
        _driverCandidates = new Lazy<LibGpiodDriverVersion[]>(GetDriverCandidates, LazyThreadSafetyMode.PublicationOnly);
        _automaticallySelectedDriverVersion = new Lazy<LibGpiodDriverVersion?>(GetAutomaticallySelectedDriverVersion, LazyThreadSafetyMode.PublicationOnly);
        _driverVersionEnvVarValue = new Lazy<string?>(GetDriverVersionEnvVarValue, LazyThreadSafetyMode.PublicationOnly);
        _driverVersionSetByEnvVar = new Lazy<LibGpiodDriverVersion?>(GetDriverVersionSetByEnvVar, LazyThreadSafetyMode.PublicationOnly);
    }

    /// <summary>
    /// Singleton instance.
    /// </summary>
    public static readonly LibGpiodDriverFactory Instance = new();

    /// <summary>
    /// A collection of driver versions that correspond to the installed versions of libgpiod on this system. Each driver is dependent
    /// on specific libgpiod version/s. If the collection is empty, it indicates that libgpiod might not be installed or could not be detected.
    /// </summary>
    public LibGpiodDriverVersion[] DriverCandidates => _driverCandidates.Value;

    public VersionedLibgpiodDriver Create(int chipNumber)
    {
        if (DriverVersionEnvVarValue != null)
        {
            if (DriverVersionSetByEnvVar == null)
            {
                throw new GpiodException($"Can not create libgpiod driver due to invalid specified value in environment variable" +
                    $" {DriverVersionEnvVar}: '{DriverVersionEnvVarValue}'" +
                    $". Valid values: {string.Join(", ", _libraryToDriverVersionMap.Values.Distinct())}");
            }

            var version = DriverVersionSetByEnvVar.Value;
            var driver = CreateInternal(DriverVersionSetByEnvVar.Value, chipNumber);

            return new VersionedLibgpiodDriver(version, driver);
        }

        return CreateAutomaticallyChosenDriver(chipNumber);
    }

    public VersionedLibgpiodDriver Create(int chipNumber, LibGpiodDriverVersion driverVersion)
    {
        return new VersionedLibgpiodDriver(driverVersion, CreateInternal(driverVersion, chipNumber));
    }

    private VersionedLibgpiodDriver CreateAutomaticallyChosenDriver(int chipNumber)
    {
        if (AutomaticallySelectedDriverVersion == null)
        {
            throw new GpiodException($"No supported libgpiod library file found.\n" +
                $"Supported library files: {string.Join(", ", _libraryToDriverVersionMap.Keys)}\n" +
                $"Searched paths: {string.Join(", ", _librarySearchPaths)}");
        }

        var version = AutomaticallySelectedDriverVersion.Value;
        var driver = CreateInternal(AutomaticallySelectedDriverVersion.Value, chipNumber);

        return new VersionedLibgpiodDriver(version, driver);
    }

    private GpioDriver CreateInternal(LibGpiodDriverVersion version, int chipNumber)
    {
        if (!DriverCandidates.Contains(version))
        {
            string installedLibraryFiles = InstalledLibraries.Any() ? string.Join(", ", InstalledLibraries) : "None";
            throw new GpiodException($"No suitable libgpiod library file found for {nameof(LibGpiodDriverVersion)}.{version} " +
                $"which requires one of: {string.Join(", ", _driverVersionToLibrariesMap[version])}\n" +
                $"Installed library files: {installedLibraryFiles}\n" +
                $"Searched paths: {string.Join(", ", _librarySearchPaths)}");
        }

        return version switch
        {
            LibGpiodDriverVersion.V1 => new LibGpiodV1Driver(chipNumber),
            LibGpiodDriverVersion.V2 => new LibGpiodV2Driver(LibGpiodProxyFactory.CreateChip(chipNumber)),
            _ => throw new ArgumentOutOfRangeException(nameof(version), version, null)
        };
    }

    private IEnumerable<string> GetInstalledLibraries()
    {
        HashSet<string> foundLibrariesFileName = new();

        foreach (string searchPath in _librarySearchPaths)
        {
            if (!Directory.Exists(searchPath))
            {
                continue;
            }

            foundLibrariesFileName.UnionWith(Directory.GetFiles(searchPath, LibrarySearchPattern));
        }

        HashSet<string> supportedLibraryVersions = new();

        foreach (string libraryFileName in foundLibrariesFileName)
        {
            foreach (string knownLibraryName in _libraryToDriverVersionMap.Keys)
            {
                if (libraryFileName.Contains(knownLibraryName))
                {
                    supportedLibraryVersions.Add(knownLibraryName);
                    break;
                }
            }
        }

        return supportedLibraryVersions;
    }

    private LibGpiodDriverVersion[] GetDriverCandidates()
    {
        return InstalledLibraries.Where(installedVersion => _libraryToDriverVersionMap.ContainsKey(installedVersion))
                                 .Select(installedVersion => _libraryToDriverVersionMap[installedVersion]).ToArray();
    }

    private LibGpiodDriverVersion? GetAutomaticallySelectedDriverVersion()
    {
        return DriverCandidates.Any() ? DriverCandidates.Max() : null;
    }

    private string? GetDriverVersionEnvVarValue()
    {
        return Environment.GetEnvironmentVariable(DriverVersionEnvVar);
    }

    private LibGpiodDriverVersion? GetDriverVersionSetByEnvVar()
    {
        if (DriverVersionEnvVarValue != null)
        {
            if (DriverVersionEnvVarValue == LibGpiodDriverVersion.V1.ToString())
            {
                return LibGpiodDriverVersion.V1;
            }

            if (DriverVersionEnvVarValue == LibGpiodDriverVersion.V2.ToString())
            {
                return LibGpiodDriverVersion.V2;
            }
        }

        return null;
    }

    public sealed record VersionedLibgpiodDriver(LibGpiodDriverVersion DriverVersion, GpioDriver LibGpiodDriver);
}
