"""Compile and exercise actual C# clients generated in each compatibility mode."""
import pathlib
import subprocess
import sys
import tempfile

generator = pathlib.Path(sys.argv[1]).resolve()
spec = pathlib.Path(sys.argv[2]).resolve()
root = pathlib.Path(tempfile.mkdtemp(prefix="kiota-csharp-readonly-smoke-"))
packages = ["Abstractions", "Serialization.Form", "Serialization.Json",
            "Serialization.Multipart", "Serialization.Text"]
for mode, flags, private in [("normal", [], True), ("backing", ["-b"], False),
                             ("backing-ebc", ["-b", "--ebc"], True)]:
    output = root / mode
    output.mkdir()
    command = ["dotnet", str(generator)] if generator.suffix == ".dll" else [str(generator)]
    subprocess.run(command + ["generate", "-d", str(spec), "-l", "CSharp", "-n", "Smoke.Client",
                              "-c", "ApiClient", "-o", str(output / "client")] + flags,
                   cwd=output, check=True)
    references = "".join(f'<PackageReference Include="Microsoft.Kiota.{p}" Version="2.1.2" />'
                         for p in packages)
    (output / "Smoke.csproj").write_text(
        '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType>'
        '<TargetFramework>net10.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings>'
        '<Nullable>enable</Nullable></PropertyGroup><ItemGroup>' + references + '</ItemGroup></Project>')
    (output / "Program.cs").write_text('''
using System.Text;
using Smoke.Client.Models;
using Microsoft.Kiota.Serialization.Json;
var setter = typeof(Item).GetProperty(nameof(Item.AveragePriceGross))!.SetMethod!;
if (setter.IsPrivate != EXPECT_PRIVATE) throw new Exception("Wrong setter visibility");
using var input = new MemoryStream(Encoding.UTF8.GetBytes("{\\"averagePriceGross\\":42.5,\\"name\\":\\"original\\"}"));
var node = await new JsonParseNodeFactory().GetRootParseNodeAsync("application/json", input);
var item = node.GetObjectValue<Item>(Item.CreateFromDiscriminatorValue)!;
if (item.AveragePriceGross != 42.5 || item.Name != "original") throw new Exception("Deserialization failed");
item.Name = "changed";
using var writer = new JsonSerializationWriter();
writer.WriteObjectValue(null, item);
using var content = writer.GetSerializedContent();
using var reader = new StreamReader(content);
var json = reader.ReadToEnd();
if (json.Contains("averagePriceGross") || !json.Contains("changed")) throw new Exception("Read-only serialization contract changed");
Console.WriteLine("MODE: setter visibility, deserialization and serialization passed");
'''.replace("EXPECT_PRIVATE", str(private).lower()).replace("MODE", mode))
    subprocess.run(["dotnet", "run", "--project", str(output / "Smoke.csproj"), "--verbosity", "quiet"],
                   cwd=output, check=True)
print("Generated-client smoke checks passed in all three modes:", root)
