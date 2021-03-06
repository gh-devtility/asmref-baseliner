﻿// Copyright (c) 2020 Devtility.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the repo root for license information.

using DumpAsmRefs.Interfaces;
using DumpAsmRefs.Tests.Infrastructure;
using FluentAssertions;
using Moq;
using System;
using System.Linq;
using Xunit;

namespace DumpAsmRefs.Tests
{
    public class CompareAsmRefReportFilesTests
    {
        [Theory]
        [InlineData("invalid compat level", "yyy", false)]
        [InlineData("Any", "xxx", true)]
        public void Compare_InvalidVersionCompatibility_Fails(string sourceVersionCompatString, string targetVersionCompatString,
            bool isSourceValid)
        {
            var dummyFileSystem = new FakeFileSystem();
            var mockLoader = new Mock<IReportLoader>();
            var mockComparer = new Mock<IResultComparer>();

            dummyFileSystem.AddFile("file1", "");
            dummyFileSystem.AddFile("file2", "");

            var buildEngine = new FakeBuildEngine();

            var testSubject = new CompareAsmRefReportFiles(dummyFileSystem, mockLoader.Object, mockComparer.Object)
            {
                BaselineReportFilePath = "file1",
                CurrentReportFilePath = "file2",
                SourceVersionCompatibility = sourceVersionCompatString,
                TargetVersionCompatibility = targetVersionCompatString,
                BuildEngine = buildEngine
            };

            // Test
            bool result = testSubject.Execute();

            // Check
            result.Should().BeFalse();
            buildEngine.ErrorEvents.Count.Should().Be(1);

            var expectedMessage = isSourceValid ? targetVersionCompatString : sourceVersionCompatString;
            buildEngine.ErrorEvents[0].Message.Contains(expectedMessage).Should().BeTrue();
        }

        [Theory]
        [InlineData(true, true, /* expected task result */ true)]
        [InlineData(true, false, /* expected task result */ true)]
        [InlineData(false, true, /* expected task result */ false)]
        [InlineData(false, false, /* expected task result */ true)]
        public void Compare_ComparerReturnsX_CheckTaskSuccess(bool comparerReturnValue, bool raiseErrorIfDifferent, bool expectedTaskResult)
        {
            var dummyFileSystem = new FakeFileSystem();
            var mockLoader = new Mock<IReportLoader>(MockBehavior.Strict);
            var mockComparer = new Mock<IResultComparer>(MockBehavior.Strict);

            dummyFileSystem.AddFile("c:\\file1.txt", "aaa");
            dummyFileSystem.AddFile("c:\\file2.txt", "bbb");

            var result1 = new AsmRefResult(new InputCriteria(), Array.Empty<SourceAssemblyInfo>());
            var result2 = new AsmRefResult(new InputCriteria(), Array.Empty<SourceAssemblyInfo>());

            mockLoader.Setup(l => l.Load("aaa")).Returns(result1);
            mockLoader.Setup(l => l.Load("bbb")).Returns(result2);

            ComparisonOptions actualOptions = null;
            mockComparer.Setup(c => c.AreSame(result1, result2, It.IsAny<ComparisonOptions>()))
                .Callback((AsmRefResult r1, AsmRefResult r2, ComparisonOptions options) => actualOptions = options)
                .Returns(comparerReturnValue);

            var buildEngine = new FakeBuildEngine();

            var testSubject = new CompareAsmRefReportFiles(dummyFileSystem,
                mockLoader.Object, mockComparer.Object)
            {
                BaselineReportFilePath = "c:\\file1.txt",
                CurrentReportFilePath = "c:\\file2.txt",
                SourceVersionCompatibility = "MajorMinorBuild",
                IgnoreSourcePublicKeyToken = true,
                TargetVersionCompatibility = "Any",
                BuildEngine = buildEngine,

                RaiseErrorIfDifferent = raiseErrorIfDifferent
            };

            testSubject.Execute().Should().Be(expectedTaskResult);
            mockLoader.VerifyAll();
            mockComparer.VerifyAll();
            actualOptions.SourceVersionCompatibility.Should().Be(VersionCompatibility.MajorMinorBuild);
            actualOptions.IgnoreSourcePublicKeyToken.Should().BeTrue();
            actualOptions.TargetVersionCompatibility.Should().Be(VersionCompatibility.Any);

            testSubject.ReportsAreSame.Should().Be(comparerReturnValue);

            if (expectedTaskResult)
            {
                buildEngine.ErrorEvents.Should().BeEmpty();
            }
            else
            {
                buildEngine.ErrorEvents.Count.Should().Be(1);
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Compare_EndToEnd_Same_NoError(bool raiseErrorIfDifferent)
        {
            // Not expecting the value of RaiseErrorIfDifferent to make any
            // difference when the reports are the same

            var dummyFileSystem = new FakeFileSystem();
            const string reportContent = @"---

# Base directory: d:\repos\devtility\asmref-baseliner
Include patterns:
- src\DumpAsmRefs\bin\Debug\net461\DumpAsmRefs.exe
Exclude patterns:
- src\**.dll
- xxx\yyy.dll
# Number of matches: 2

---

Assembly: DumpAsmRefs, Version=0.8.0.0, Culture=neutral, PublicKeyToken=null
Relative path: src/DumpAsmRefs/bin/Debug/net461/DumpAsmRefs.exe

Referenced assemblies:   # count = 2
- Microsoft.Build.Framework, Version=14.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
- System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089

---

Assembly: Assembly2, Version=1.2.3.4, Culture=neutral, PublicKeyToken=null
Relative path: asm2/bin/Debug/net461/assembly2.dll

Referenced assemblies:   # count = 1
- mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089

...";

            dummyFileSystem.AddFile("file1", reportContent);
            dummyFileSystem.AddFile("file2", reportContent);

            var buildEngine = new FakeBuildEngine();

            var testSubject = new CompareAsmRefReportFiles(dummyFileSystem, 
                new YamlReportLoader(), new AsmRefResultComparer())
            {
                BaselineReportFilePath = "file1",
                CurrentReportFilePath = "file2",
                SourceVersionCompatibility = "sTRICt",
                BuildEngine = buildEngine,
                TargetVersionCompatibility = "any",

                // Test input value
                RaiseErrorIfDifferent = raiseErrorIfDifferent
            };

            // Test
            bool result = testSubject.Execute();

            // Check
            result.Should().BeTrue();
            testSubject.ReportsAreSame.Should().BeTrue();

            buildEngine.ErrorEvents.Count.Should().Be(0);
            buildEngine.MessageEvents.Count.Should().Be(5);
            buildEngine.MessageEvents[1].Message.Contains("Source").Should().BeTrue();
            buildEngine.MessageEvents[1].Message.Contains(": Strict").Should().BeTrue();

            buildEngine.MessageEvents[2].Message.Contains("Target").Should().BeTrue();
            buildEngine.MessageEvents[2].Message.Contains(": Any").Should().BeTrue();

            buildEngine.MessageEvents[3].Message.Contains(": False").Should().BeTrue();
            buildEngine.MessageEvents[4].Message.Contains(": file1").Should().BeTrue();
            buildEngine.MessageEvents[4].Message.Contains(": file2").Should().BeTrue();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Compare_EndToEnd_Different_CheckResultDependsOnValueOfRaiseErrorIfDifferent(bool raiseErrorIfDifferent)
        {
            var reportContent1 = @"---
Include patterns:
- src\DumpAsmRefs\bin\Debug\net461\DumpAsmRefs.exe
Exclude patterns:
- src\**.dll

---

Assembly: DumpAsmRefs, Version=0.8.0.0, Culture=neutral, PublicKeyToken=null
Relative path: src/DumpAsmRefs/bin/Debug/net461/DumpAsmRefs.exe

Referenced assemblies:   # count = 2
- Microsoft.Build.Framework, Version=14.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a
- System, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089

...";

            var reportContent2 = @"---

Include patterns:
- src\DumpAsmRefs\bin\Debug\net461\DumpAsmRefs.exe
Exclude patterns:
- src\**.dll

---

Assembly: DumpAsmRefs, Version=0.8.0.0, Culture=neutral, PublicKeyToken=null
Relative path: src/DumpAsmRefs/bin/Debug/net461/DumpAsmRefs.exe

Referenced assemblies:   # count = 1
- Microsoft.Build.Framework, Version=14.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a

...";

            var dummyFileSystem = new FakeFileSystem();
            dummyFileSystem.AddFile("file1", reportContent1);
            dummyFileSystem.AddFile("file2", reportContent2);

            var buildEngine = new FakeBuildEngine();

            var testSubject = new CompareAsmRefReportFiles(dummyFileSystem,
                new YamlReportLoader(), new AsmRefResultComparer())
            {
                BaselineReportFilePath = "file1",
                CurrentReportFilePath = "file2",
                SourceVersionCompatibility = "any",
                BuildEngine = buildEngine,
                TargetVersionCompatibility = "any",

                // Test input parameter
                RaiseErrorIfDifferent = raiseErrorIfDifferent
            };

            // Test
            bool result = testSubject.Execute();

            // Check
            testSubject.ReportsAreSame.Should().BeFalse();

            if (raiseErrorIfDifferent)
            {
                // Task should fail, error should be logged
                result.Should().BeFalse();
                buildEngine.ErrorEvents.Count.Should().Be(1);
                buildEngine.ErrorEvents[0].Message.Contains("file1").Should().BeTrue();
                buildEngine.ErrorEvents[0].Message.Contains("file2").Should().BeTrue();
            }
            else
            {
                // Task should succeed, message should be logged
                result.Should().BeTrue();
                buildEngine.ErrorEvents.Count.Should().Be(0);
                buildEngine.MessageEvents.Last().Message.Contains("file1").Should().BeTrue();
                buildEngine.MessageEvents.Last().Message.Contains("file2").Should().BeTrue();
            }
        }
    }
}
