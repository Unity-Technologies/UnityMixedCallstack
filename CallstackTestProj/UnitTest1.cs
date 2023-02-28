using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityMixedCallStack;

namespace CallstackTestProj
{
	[TestFixture]
	public class Tests
	{
        private Tuple<string, string>[] m_legacyData =
        {
            Tuple.Create("000001C44A1C81D7", "[UnityEditor.CoreModule.dll] (wrapper managed-to-native) UnityEditor.EditorGUIUtility:RenderPlayModeViewCamerasInternal_Injected (UnityEngine.RenderTexture,int,UnityEngine.Vector2&,bool,bool)"),
            Tuple.Create("000001C44A1C8083", "[UnityEditor.CoreModule.dll] UnityEditor.EditorGUIUtility:RenderPlayModeViewCamerasInternal (UnityEngine.RenderTexture,int,UnityEngine.Vector2,bool,bool)"),
            Tuple.Create("000001C44A1C52EB", "[UnityEditor.CoreModule.dll] UnityEditor.PlayModeView:RenderView (UnityEngine.Vector2,bool)"),
            Tuple.Create("000001C449F19A23", "[UnityEditor.CoreModule.dll] UnityEditor.GameView:OnGUI ()"),
            Tuple.Create("000001C449F12BF8", "[UnityEditor.CoreModule.dll] UnityEditor.HostView:InvokeOnGUI (UnityEngine.Rect)"),
            Tuple.Create("000001C449F12793", "[UnityEditor.CoreModule.dll] UnityEditor.DockArea:DrawView (UnityEngine.Rect)"),
            Tuple.Create("000001C449EF1353", "[UnityEditor.CoreModule.dll] UnityEditor.DockArea:OldOnGUI ()"),
            Tuple.Create("000001C449EBCCE9", "[UnityEngine.UIElementsModule.dll] UnityEngine.UIElements.IMGUIContainer:DoOnGUI (UnityEngine.Event,UnityEngine.Matrix4x4,UnityEngine.Rect,bool,UnityEngine.Rect,System.Action,bool)"),
            Tuple.Create("000001C449EBAC13", "[UnityEngine.UIElementsModule.dll] UnityEngine.UIElements.IMGUIContainer:HandleIMGUIEvent (UnityEngine.Event,UnityEngine.Matrix4x4,UnityEngine.Rect,System.Action,bool)")
        };

        [Test]
        public void LegacyDataTest()
        {
            Assert.IsTrue(PmipReader.ReadPmipFile(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "pmip_32260_3.txt")));
            PmipReader.Sort();

            foreach (Tuple<string, string> t in m_legacyData)
			{
                if (PmipReader.TryGetDescriptionForIp(ulong.Parse(t.Item1, NumberStyles.HexNumber), out string retVal))
                {
                    Assert.AreEqual(t.Item2, retVal);
                }
                else
                    Assert.Fail();
			}
            PmipReader.DisposeStreams();
        }

        private Tuple<string, string>[] m_legacyModeData =
        {
            Tuple.Create("000001C4F5DC03FF", "[Assembly-CSharp.dll] SpinMe:FooBar ()"),
            Tuple.Create("000001C4F5DC02D3", "[Assembly-CSharp.dll] SpinMe:Foo ()"),
            Tuple.Create("000001C4F5DBF6F3", "[Assembly-CSharp.dll] SpinMe:Update ()"),
            Tuple.Create("000001C3FF868578", "[mscorlib.dll] (wrapper runtime-invoke) object:runtime_invoke_void__this__ (object,intptr,intptr,intptr)")
        };

        [Test]
        public void LegacyModeDataTest()
		{
            Assert.IsTrue(PmipReader.ReadPmipFile(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "pmip_31964_3.txt")));
            PmipReader.Sort();

            foreach (Tuple<string, string> t in m_legacyModeData)
            {
                if (PmipReader.TryGetDescriptionForIp(ulong.Parse(t.Item1, NumberStyles.HexNumber), out string retVal))
                {
                    Assert.AreEqual(t.Item2, retVal);
                }
                else
                    Assert.Fail();
            };
            PmipReader.DisposeStreams();
        }

        private Tuple<string, string>[] m_lineNumbersData =
        {
            Tuple.Create("00000244C7A990BF", "[Assembly-CSharp.dll] SpinMe:FooBar ()"),
            Tuple.Create("00000244C7A98F93", "[Assembly-CSharp.dll] SpinMe:Foo () : 32"),
            Tuple.Create("00000244C7A98153", "[Assembly-CSharp.dll] SpinMe:Update () : 26"),
            Tuple.Create("00000244EC9DF6B8", "[mscorlib.dll] (wrapper runtime-invoke) object:runtime_invoke_void__this__ (object,intptr,intptr,intptr)")
        };

        [Test]
        public void LineNumbersDataTest()
        {
            Assert.IsTrue(PmipReader.ReadPmipFile(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "line-numbers", "pmip_23672_1_0.txt")));
            Assert.IsTrue(PmipReader.ReadPmipFile(Path.Combine(TestContext.CurrentContext.TestDirectory, "TestData", "line-numbers", "pmip_23672_4_1.txt")));
            PmipReader.Sort();

            foreach (Tuple<string, string> t in m_lineNumbersData)
            {
                if (PmipReader.TryGetDescriptionForIp(ulong.Parse(t.Item1, NumberStyles.HexNumber), out string retVal))
                {
                    Assert.AreEqual(t.Item2, retVal);
                }
                else
                    Assert.Fail($"Couldn't find address: {t.Item1}");
            };

            PmipReader.DisposeStreams();
        }
    }
}