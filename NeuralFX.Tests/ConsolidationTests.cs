using System.Security.Cryptography;
using System.Text;
using NeuralFX.Hub.Models;
using NeuralFX.Hub.Services;
using NeuralFX.Protocol;
namespace NeuralFX.Tests;
public class ConsolidationTests
{
    [Theory]
    [InlineData("nvngx_dlss.dll")]
    [InlineData("nvngx_dlssnr.dll")]
    public void BothNvidiaRuntimesRequireSuccessfulTrust(string name)
    {
        BinaryIdentity.ValidateTrustResult(0,name);
        Assert.Throws<CryptographicException>(()=>BinaryIdentity.ValidateTrustResult(unchecked((int)0x80096010),name));
        Assert.Throws<CryptographicException>(()=>BinaryIdentity.ValidateTrustResult(unchecked((int)0x800B0109),name));
    }
    [Fact]
    public async Task DirectFileChecksItsHashRatherThanArchiveHash()
    {
        string root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try {
            byte[] bytes=Encoding.UTF8.GetBytes("direct-payload");string input=Path.Combine(root,"input.dll");File.WriteAllBytes(input,bytes);
            var item=new DependencyItem {Id="test",SourceType="PublicDownload",CanAutoDownload=true,DownloadType="ZipExtract",ExpectedSha256=new string('a',64),TargetRelativePath="test.dll",ExpectedFileSha256=new(){["test.dll"]=DependencyManagerService.Hash(bytes)}};
            var manager=new DependencyManagerService(Path.Combine(root,"store"));
            Assert.True(await manager.ImportFileAsync(item,input),item.StatusMessage);
            Assert.Equal(bytes,manager.ReadPayload(item)["test.dll"]);
            File.WriteAllText(input,"bad");Assert.False(await manager.ImportFileAsync(item,input));
        } finally {Directory.Delete(root,true);}
    }
    [Fact]
    public void InsertionKeepsForeignTechniquesOnTheirOriginalSides()
    {
        string[] ours={"Lumenite_Kernel@lumenite_Kernel.fx","DLSS5_Feed@DLSS5_Feed.fx","NeuralFX_CAS@NeuralFX_CAS.fx"};
        string result=PipelineConfiguration.InsertOwned("Before@a.fx,DLSS5_Feed@DLSS5_Feed.fx,CAS@user.fx,After@b.fx",ours);
        Assert.StartsWith("Before@a.fx,Lumenite_Kernel",result);Assert.EndsWith("CAS@user.fx,After@b.fx",result);
        Assert.Equal(result,PipelineConfiguration.InsertOwned(result,ours));
    }
    [Fact]
    public void ConflictingIniDuplicatesRequireReview()
    {
        Assert.Throws<InvalidDataException>(()=>Ini.Set("[GENERAL]\nPresetPath=a.ini\nPresetPath=b.ini","GENERAL","PresetPath","c.ini"));
        string normalized=Ini.Set("A=1\nA=1","","A","2");Assert.Equal(1,normalized.Split('\n').Count(x=>x.StartsWith("A=")));
    }
    [Fact]
    public void NgxAndGpuOutputNeverImplyNr()
    {
        var frame=new TelemetryFrame {Flags=RuntimeFlags.CameraPresent|RuntimeFlags.PipelineRequested|RuntimeFlags.NativeConnected|RuntimeFlags.EvaluationSucceeded|RuntimeFlags.OutputCommitted};
        Assert.Equal("Portador NGX operativo; NR sin confirmar",SessionViewState.Summary(frame));
        frame.Flags &= ~RuntimeFlags.PipelineRequested;Assert.Equal("Desactivado por el usuario",SessionViewState.Summary(frame));
    }
    [Fact]
    public void ReviewedPlanRejectsFilesChangedAfterPreview()
    {
        string root=Path.Combine(Path.GetTempPath(),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        try {var preview=new InstallationPreview();preview.ExpectedBefore["test.ini"]=null;preview.ValidateUnchanged(root);File.WriteAllText(Path.Combine(root,"test.ini"),"external");Assert.Throws<IOException>(()=>preview.ValidateUnchanged(root));}
        finally {Directory.Delete(root,true);}
    }
}
