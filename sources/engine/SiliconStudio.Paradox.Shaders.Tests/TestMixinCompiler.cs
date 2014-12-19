﻿// Copyright (c) 2014 Silicon Studio Corp. (http://siliconstudio.co.jp)
// This file is distributed under GPL v3. See LICENSE.md for details.
using System.IO;
using System.Linq;
using NUnit.Framework;

using SiliconStudio.Core.IO;
using SiliconStudio.Core.Mathematics;
using SiliconStudio.Core.Serialization.Assets;
using SiliconStudio.Core.Storage;
using SiliconStudio.Paradox.Assets.Materials;
using SiliconStudio.Paradox.Assets.Materials.Nodes;
using SiliconStudio.Paradox.Effects;
using SiliconStudio.Paradox.Graphics;
using SiliconStudio.Paradox.Shaders.Compiler;
using SiliconStudio.Paradox.Shaders.Parser.Mixins;

namespace SiliconStudio.Paradox.Shaders.Tests
{
    /// <summary>
    /// Tests for the mixins code generation and runtime API.
    /// </summary>
    [TestFixture]
    public partial class TestMixinCompiler
    {
        /// <summary>
        /// Tests mixin and compose keys with compilation.
        /// </summary>
        [Test]
        public void TestMaterial()
        {
            var compiler = new EffectCompiler { UseFileSystem = true };
            compiler.SourceDirectories.Add(@"..\..\sources\engine\SiliconStudio.Paradox.Graphics\Shaders");
            compiler.SourceDirectories.Add(@"..\..\sources\shaders\Materials");
            compiler.SourceDirectories.Add(@"..\..\sources\shaders\ComputeColor");
            var compilerParameters = new CompilerParameters { Platform = GraphicsPlatform.Direct3D11 };

            var materialAsset = new MaterialAsset
            {
                Composition = new MaterialFeatures()
                {
                    Surface = new MaterialNormalMapFeature()
                    {
                        NormalMap = new MaterialColorNode(Color4.White)
                    }
                }
            };

            var shaderClassSource = MaterialShaderGenerator.Generate(materialAsset);

            var mixin = new ShaderMixinSource();
            mixin.Mixins.Add(new ShaderClassSource("MaterialLayerRoot"));
            mixin.AddComposition("composition", shaderClassSource);
            var results = compiler.Compile(mixin, compilerParameters);

            Assert.IsFalse(results.HasErrors);
        }


        /// <summary>
        /// Tests mixin and compose keys with compilation.
        /// </summary>
        [Test]
        public void TestMixinAndComposeKeys()
        {
            var compiler = new EffectCompiler { UseFileSystem = true };
            compiler.SourceDirectories.Add(@"..\..\sources\engine\SiliconStudio.Paradox.Graphics\Shaders");
            compiler.SourceDirectories.Add(@"..\..\sources\engine\SiliconStudio.Paradox.Shaders.Tests\GameAssets\Mixins");

            var compilerParameters = new CompilerParameters {Platform = GraphicsPlatform.Direct3D11};

            var subCompute1Key = TestABC.TestParameters.UseComputeColor2.ComposeWith("SubCompute1");
            var subCompute2Key = TestABC.TestParameters.UseComputeColor2.ComposeWith("SubCompute2");
            var subComputesKey = TestABC.TestParameters.UseComputeColorRedirect.ComposeWith("SubComputes[0]");

            compilerParameters.Set(subCompute1Key, true);
            compilerParameters.Set(subComputesKey, true);

            var results = compiler.Compile(new ShaderMixinGeneratorSource("test_mixin_compose_keys"), compilerParameters);

            Assert.IsFalse(results.HasErrors);

            Assert.NotNull(results.MainBytecode.Reflection.ConstantBuffers);
            Assert.AreEqual(1, results.MainBytecode.Reflection.ConstantBuffers.Count);
            var cbuffer = results.MainBytecode.Reflection.ConstantBuffers[0];

            Assert.NotNull(cbuffer.Members);
            Assert.AreEqual(2, cbuffer.Members.Length);


            // Check that ComputeColor2.Color is correctly composed for variables
            var computeColorSubCompute2 = ComputeColor2Keys.Color.ComposeWith("SubCompute1");
            var computeColorSubComputes = ComputeColor2Keys.Color.ComposeWith("ColorRedirect.SubComputes[0]");

            var members = cbuffer.Members.Select(member => member.Param.KeyName).ToList();
            Assert.IsTrue(members.Contains(computeColorSubCompute2.Name));
            Assert.IsTrue(members.Contains(computeColorSubComputes.Name));
        }

        private void CopyStream(DatabaseFileProvider database, string fromFilePath)
        {
            var shaderFilename = string.Format("shaders/{0}", Path.GetFileName(fromFilePath));
            using (var outStream = database.OpenStream(shaderFilename, VirtualFileMode.Create, VirtualFileAccess.Write, VirtualFileShare.Write))
            {
                using (var inStream = new FileStream(fromFilePath, FileMode.Open, FileAccess.Read))
                {
                    inStream.CopyTo(outStream);
                }
            }
        }

        /// <summary>
        /// Tests a simple mixin.
        /// </summary>
        [Test]
        public void TestReal()
        {
            // TODO: review this tet
            if (Directory.Exists("data"))
            {
                Directory.Delete("data", true);
            }

            CompilerResults results01;
            CompilerResults results02;

            // Test this with a new database completely clean
            TestNoClean(out results01, out results02);
            Assert.That(results01.Bytecodes.Count, Is.EqualTo(results02.Bytecodes.Count));
            Assert.That(results01.MainBytecode, Is.EqualTo(results02.MainBytecode));

            // Test with the database with already the bytecode generated by previous step
            CompilerResults results11;
            CompilerResults results12;
            TestNoClean(out results11, out results12);
            Assert.That(results11.Bytecodes.Count, Is.EqualTo(results12.Bytecodes.Count));
            Assert.That(results11.MainBytecode, Is.EqualTo(results12.MainBytecode));

            // Check previous result
            Assert.That(results01.Bytecodes.Count, Is.EqualTo(results11.Bytecodes.Count));
            //Assert.That(results01.MainBytecode.Time, Is.EqualTo(results11.MainBytecode.Time)); -> crash, MainBytecode == null
        }

        public void TestNoClean(out CompilerResults left, out CompilerResults right)
        {
            // Create and mount database file system
            var objDatabase = new ObjectDatabase("/data/db", "index", "/local/db");
            using (var assetIndexMap = AssetIndexMap.Load())
            {
                var database = new DatabaseFileProvider(assetIndexMap, objDatabase);
                AssetManager.GetFileProvider = () => database;

                foreach (var shaderName in Directory.EnumerateFiles(@"..\..\sources\shaders", "*.pdxsl"))
                    CopyStream(database, shaderName);

                foreach (var shaderName in Directory.EnumerateFiles(@"..\..\sources\engine\SiliconStudio.Paradox.Shaders.Tests\GameAssets\Compiler", "*.pdxsl"))
                    CopyStream(database, shaderName);

                var compiler = new EffectCompiler();
                compiler.SourceDirectories.Add("assets/shaders");
                var compilerCache = new EffectCompilerCache(compiler);

                var compilerParameters = new CompilerParameters {Platform = GraphicsPlatform.Direct3D11};

                left = compilerCache.Compile(new ShaderMixinGeneratorSource("SimpleEffect"), compilerParameters);
                right = compilerCache.Compile(new ShaderMixinGeneratorSource("SimpleEffect"), compilerParameters);
            }
        }

        [Test]
        public void TestGlslCompiler()
        {
            VirtualFileSystem.RemountFileSystem("/shaders", "../../../../shaders");
            VirtualFileSystem.RemountFileSystem("/baseShaders", "../../../../engine/SiliconStudio.Paradox.Graphics/Shaders");
            VirtualFileSystem.RemountFileSystem("/compiler", "Compiler");


            var compiler = new EffectCompiler();

            compiler.SourceDirectories.Add("shaders");
            compiler.SourceDirectories.Add("compiler");
            compiler.SourceDirectories.Add("baseShaders");

            var compilerParameters = new CompilerParameters { Platform = GraphicsPlatform.OpenGL };

            var results = compiler.Compile(new ShaderMixinGeneratorSource("ToGlslEffect"), compilerParameters);
        }

        [Test]
        public void TestGlslESCompiler()
        {
            VirtualFileSystem.RemountFileSystem("/shaders", "../../../../shaders");
            VirtualFileSystem.RemountFileSystem("/baseShaders", "../../../../engine/SiliconStudio.Paradox.Graphics/Shaders");
            VirtualFileSystem.RemountFileSystem("/compiler", "Compiler");

            var compiler = new EffectCompiler();

            compiler.SourceDirectories.Add("shaders");
            compiler.SourceDirectories.Add("compiler");
            compiler.SourceDirectories.Add("baseShaders");

            var compilerParameters = new CompilerParameters { Platform = GraphicsPlatform.OpenGLES };

            var results = compiler.Compile(new ShaderMixinGeneratorSource("ToGlslEffect"), compilerParameters);
        }
    }
}