using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Xunit;

namespace NeuralFX.Tests;

/// <summary>
/// Los contratos nativos se comprueban en C++ con <c>static_assert</c>, pero esa comprobación
/// solo corre cuando alguien ejecuta <c>Native/build.ps1</c>, que necesita el toolchain. Esta
/// suite es la puerta rápida: si un campo se añade a un lado y no al otro, falla aquí.
/// </summary>
/// <remarks>
/// No sustituye al fixture D3D11. Comprueba forma, no comportamiento.
/// </remarks>
public class NativeContractTests
{
    private static string Repository()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "Native"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return directory!.FullName;
    }

    private static int AssertedSize(string header, string type)
    {
        string text = File.ReadAllText(Path.Combine(Repository(), "Native", header));
        var match = Regex.Match(text, @"static_assert\s*\(\s*sizeof\s*\(\s*" + Regex.Escape(type) + @"\s*\)\s*==\s*(\d+)\s*\)");
        Assert.True(match.Success, "Falta el static_assert de tamaño para " + type + " en " + header);
        return int.Parse(match.Groups[1].Value);
    }

    // La estructura administrada vive en el mod, que es net35 y no se puede referenciar desde
    // aquí. Se declara su gemela con los mismos campos y el mismo empaquetado: si alguien
    // cambia una de las dos, los tamaños dejan de coincidir y el test lo dice.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct RegistrationReportMirror
    {
        public uint Size, Version, RequestSerial, Stage, Reason;
        public int HResult;
        public uint Camera, Epoch, Width, Height, Format;
        public uint MipLevels, ArraySize, SampleCount, SampleQuality;
        public uint BindFlags, MiscFlags, Usage, CpuAccess, FromView;
        public uint SlotsUsed, SlotsTotal, Accepted, Rejected;
        public uint ReservedNow, ReserveOk, ReserveDenied, Completed;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 12)] public uint[] Counts;
    }

    [Fact]
    public void RegistrationReportKeepsTheSameShapeOnBothSides()
    {
        Assert.Equal(AssertedSize("neuralfx_registration_report.h", "NeuralFxRegistrationReport"),
            Marshal.SizeOf<RegistrationReportMirror>());
    }

    [Fact]
    public void RegistrationReportDeclaresOneReasonPerRejection()
    {
        string text = File.ReadAllText(Path.Combine(Repository(), "Native", "neuralfx_registration_report.h"));
        var match = Regex.Match(text, @"NFX_REG_REASON_COUNT\s*=\s*(\d+)");
        Assert.True(match.Success);
        int declared = int.Parse(match.Groups[1].Value);
        // Cada motivo ocupa un valor propio: agrupar dos en uno es exactamente el defecto que
        // este informe vino a corregir, así que el contador tiene que cubrirlos todos.
        var reasons = Regex.Matches(text, @"NFX_REG_(?!STAGE|REASON_COUNT|OK)[A-Z_]+\s*=\s*(\d+)");
        Assert.NotEmpty(reasons);
        foreach (Match reason in reasons) Assert.True(int.Parse(reason.Groups[1].Value) < declared);
        Assert.Equal(reasons.Count + 1, declared); // +1 por NFX_REG_OK
    }

    [Fact]
    public void NativeSizeAssertionsStayInPlaceForTheSharedStructures()
    {
        Assert.Equal(64, AssertedSize("neuralfx_contract.h", "NeuralFxFrameV3"));
        Assert.Equal(80, AssertedSize("neuralfx_contract.h", "NeuralFxResult"));
        Assert.Equal(64, AssertedSize("neuralfx_contract.h", "NeuralFxHealth"));
        Assert.Equal(24, AssertedSize("neuralfx_contract.h", "NeuralFxControls"));
    }
}
