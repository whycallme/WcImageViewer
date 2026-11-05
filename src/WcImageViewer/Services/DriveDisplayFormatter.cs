using System;
using System.IO;

namespace WcImageViewer.Services;

/// <summary>폴더 트리 드라이브 노드 표시 문자열("볼륨이름 (C:)"). 노드의 실제 경로 값(Tag)은 바뀌지 않는다.</summary>
public static class DriveDisplayFormatter
{
    /// <summary>
    /// 순수 함수. 준비된 드라이브는 "{볼륨이름} (X:)", 볼륨이름이 비면 종류별 기본 이름("로컬 디스크" 등),
    /// 준비 안 된 드라이브는 예외 없이 "(X:)".
    /// </summary>
    public static string Format(string rootPath, string? volumeLabel, DriveType driveType, bool isReady)
    {
        var suffix = $"({DriveLetterOf(rootPath)})";
        if (!isReady)
        {
            return suffix;
        }

        var label = volumeLabel?.Trim();
        if (string.IsNullOrEmpty(label))
        {
            label = DefaultName(driveType);
        }

        return $"{label} {suffix}";
    }

    /// <summary>DriveInfo 에서 표시 문자열을 만든다. 어떤 속성 접근이 예외를 던져도 "(X:)" 또는 기본 이름으로 대체한다.</summary>
    public static string FormatDrive(DriveInfo drive)
    {
        var root = SafeRead(() => drive.Name, string.Empty);
        var ready = SafeRead(() => drive.IsReady, false);
        var type = SafeRead(() => drive.DriveType, DriveType.Unknown);
        string? label = null;
        if (ready)
        {
            label = SafeRead<string?>(() => drive.VolumeLabel, null);
        }

        return Format(root, label, type, ready);
    }

    private static string DefaultName(DriveType driveType) => driveType switch
    {
        DriveType.Fixed => "로컬 디스크",
        DriveType.Network => "네트워크 드라이브",
        DriveType.Removable => "이동식 디스크",
        DriveType.CDRom => "CD 드라이브",
        DriveType.Ram => "RAM 디스크",
        _ => "드라이브",
    };

    private static string DriveLetterOf(string rootPath)
    {
        var trimmed = (rootPath ?? string.Empty).Trim().TrimEnd('\\', '/');
        if (trimmed.Length >= 2 && trimmed[1] == ':')
        {
            return char.ToUpperInvariant(trimmed[0]) + ":";
        }

        return trimmed;
    }

    private static T SafeRead<T>(Func<T> read, T fallback)
    {
        try
        {
            return read();
        }
        catch
        {
            return fallback;
        }
    }
}
