// Copyright (c) 2026 DeskPilot contributors. Project MS-CL.
// Independent adapters over Windows/.NET APIs, retaining persisted XML contracts.
using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
namespace Unified
{
    public static class Serialize
    {
        public static MemoryStream SerializeBinary(object value) {throw new NotSupportedException("Binary serialization is disabled.");}
        public static byte[] SerializeBinaryAsBytes(object value) {throw new NotSupportedException("Binary serialization is disabled.");}
        public static object DeSerializeBinary(MemoryStream value) {throw new NotSupportedException("Binary deserialization is disabled.");}
        private static XmlReader Reader(Stream input) {return XmlReader.Create(input,new XmlReaderSettings {DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null,MaxCharactersInDocument=67108864,CloseInput=false});}
        public static MemoryStream SerializeXML(object value) {return SerializeXML(value,new XmlSerializer(value.GetType()));}
        public static MemoryStream SerializeXML(object value,Type type,bool throwException) {try{return SerializeXML(value,new XmlSerializer(type));}catch {if(throwException)throw;return null;}}
        public static MemoryStream SerializeXML(object value,XmlSerializer serializer) {var stream=new MemoryStream();try {serializer.Serialize(stream,value);stream.Position=0;return stream;}catch{stream.Dispose();throw;}}
        public static byte[] SerializeXMLAsBytes(object value) {using(var stream=SerializeXML(value))return stream.ToArray();}
        public static string SerializeXMLAsString(object value) {return SerializeXMLAsString(value,new XmlSerializer(value.GetType()));}
        public static string SerializeXMLAsString(object value,XmlSerializer serializer) {using(var stream=SerializeXML(value,serializer))using(var reader=new StreamReader(stream,Encoding.UTF8,true))return reader.ReadToEnd();}
        public static object DeSerializeXML(MemoryStream stream,Type type,bool throwException) {try{if(stream==null)return null;stream.Position=0;using(var reader=Reader(stream))return new XmlSerializer(type).Deserialize(reader);}catch{if(throwException)throw;return null;}}
        public static object DeSerializeXML(MemoryStream stream,Type type) {return DeSerializeXML(stream,type,false);}
        public static object DeSerializeXML(string xml,Type type,bool throwException) {try {return DeSerializeXML(xml,new XmlSerializer(type));}catch{if(throwException)throw;return null;}}
        public static object DeSerializeXML(string xml,Type type) {return DeSerializeXML(xml,type,false);}
        public static object DeSerializeXML(string xml,XmlSerializer serializer) {using(var stream=new MemoryStream(Encoding.UTF8.GetBytes(xml)))using(var reader=Reader(stream))return serializer.Deserialize(reader);}
        public static void SerializeXMLToDisk(object value,string path) {SerializeXMLToDisk(value,path,new XmlSerializer(value.GetType()));}
        public static void SerializeXMLToDisk(object value,string path,XmlAttributeOverrides overrides) {SerializeXMLToDisk(value,path,new XmlSerializer(value.GetType(),overrides));}
        public static void SerializeXMLToDisk(object value,string path,XmlSerializer serializer) {byte[] bytes;using(var stream=SerializeXML(value,serializer))bytes=stream.ToArray();File.WriteAllBytes(path,bytes);}
        public static object DeserializeXMLFromDisk(string path,Type type) {return DeserializeXMLFromDisk(path,type,new XmlAttributeOverrides());}
        public static object DeserializeXMLFromDisk(string path,Type type,XmlAttributeOverrides overrides) {return DeserializeXMLFromDisk(path,type,new XmlSerializer(type,overrides));}
        public static object DeserializeXMLFromDisk(string path,Type type,XmlSerializer serializer) {if(!File.Exists(path))return Activator.CreateInstance(type);using(var stream=File.OpenRead(path))using(var reader=Reader(stream))return serializer.Deserialize(reader);}
    }
    public static class StreamHelper
    {
        public static byte[] StreamToBytes(Stream stream) {if(stream==null)return null;if(stream.CanSeek)stream.Position=0;using(var copy=new MemoryStream()){stream.CopyTo(copy);return copy.ToArray();}}
        public static string StreamToString(Stream stream) {var bytes=StreamToBytes(stream);return bytes==null?null:Encoding.UTF8.GetString(bytes);}
    }
}
namespace Terminals.Forms.Rendering
{
    public enum RenderColors {Black,Blue}
    public static class Office2007Renderer
    {
        public static ToolStripRenderer GetRenderer(RenderColors colors) {return new ToolStripProfessionalRenderer(new DeskPilotColors(colors==RenderColors.Black));}
        private sealed class DeskPilotColors : ProfessionalColorTable
        {
            private readonly bool dark;internal DeskPilotColors(bool value){dark=value;UseSystemColors=false;}
            public override Color ToolStripGradientBegin {get{return dark?Color.FromArgb(28,38,51):Color.FromArgb(225,236,248);}}
            public override Color ToolStripGradientMiddle {get{return ToolStripGradientBegin;}}
            public override Color ToolStripGradientEnd {get{return ToolStripGradientBegin;}}
            public override Color MenuStripGradientBegin {get{return ToolStripGradientBegin;}}
            public override Color MenuStripGradientEnd {get{return ToolStripGradientBegin;}}
            public override Color ToolStripDropDownBackground {get{return dark?Color.FromArgb(28,38,51):Color.White;}}
            public override Color MenuItemSelected {get{return dark?Color.FromArgb(43,78,110):Color.FromArgb(202,226,247);}}
        }
    }
}
namespace IconHandler
{
    public enum IconSize {Small,Large}
    public static class IconHandler
    {
        [DllImport("shell32.dll",CharSet=CharSet.Unicode)] private static extern uint ExtractIconEx(string file,int index,IntPtr[] large,IntPtr[] small,uint count);
        [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
        public static Icon[] IconsFromFile(string file,IconSize size)
        {
            uint count=Math.Min(ExtractIconEx(file,-1,null,null,0),256);if(count==0)return new Icon[0];
            var large=new IntPtr[count];var small=new IntPtr[count];var icons=new System.Collections.Generic.List<Icon>();
            try {ExtractIconEx(file,0,large,small,count);for(int i=0;i<count;i++){var handle=size==IconSize.Small?small[i]:large[i];if(handle!=IntPtr.Zero)icons.Add((Icon)Icon.FromHandle(handle).Clone());}return icons.ToArray();}
            finally {foreach(var h in large)if(h!=IntPtr.Zero)DestroyIcon(h);foreach(var h in small)if(h!=IntPtr.Zero)DestroyIcon(h);}
        }
    }
}
