using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;

namespace Llm2Pcg.Core.V4
{
    /// <summary>Bounded, strict JSON reader for the public-field v4 DTO. No Unity or third-party dependency.</summary>
    public static class SpatialRequestJson
    {
        public static SpatialRequest Parse(string json)
        {
            if (json == null || json.Length > 65536) throw Invalid("JSON exceeds 64 Ki characters.");
            var reader = new Reader(json);
            var request = (SpatialRequest)reader.Read(typeof(SpatialRequest), 0);
            reader.End(); SpatialRequestValidator.Validate(request); return request;
        }
        private static ConstraintFailure Invalid(string message) => new ConstraintFailure("INVALID_V4_REQUEST", message);
        private sealed class Reader
        {
            private readonly string text; private int pos;
            public Reader(string value) { text = value; }
            private void Space() { while (pos < text.Length && (text[pos]==' ' || text[pos]=='\r' || text[pos]=='\n' || text[pos]=='\t')) pos++; }
            private bool Take(char c) { Space(); if (pos >= text.Length || text[pos]!=c) return false; pos++; return true; }
            private void Need(char c) { if (!Take(c)) throw Invalid("Expected JSON delimiter at " + pos); }
            public void End() { Space(); if (pos != text.Length) throw Invalid("Trailing JSON content."); }
            public object Read(Type type, int depth)
            {
                if (depth > 16) throw Invalid("JSON nesting limit.");
                Space();
                if (type == typeof(string)) return String();
                if (type == typeof(int))
                {
                    int start=pos;if(pos<text.Length && text[pos]=='-')pos++;
                    int digits=pos;
                    while(pos<text.Length && text[pos]>='0' && text[pos]<='9')pos++;
                    if(pos==digits || (pos-digits>1 && text[digits]=='0') || pos-start>11 ||
                        !int.TryParse(text.Substring(start,pos-start),NumberStyles.AllowLeadingSign,CultureInfo.InvariantCulture,out int value))
                        throw Invalid("Expected Int32 JSON integer.");
                    return value;
                }
                if(type==typeof(bool))
                {
                    foreach(string word in new[]{"true","false"})
                        if(pos+word.Length<=text.Length && string.CompareOrdinal(text,pos,word,0,word.Length)==0) {pos+=word.Length;return word=="true";}
                    throw Invalid("Expected JSON boolean.");
                }
                if(type.IsArray)
                {
                    Need('[');var values=new List<object>();Type element=type.GetElementType();
                    if(!Take(']')) while(true)
                    {
                        if(values.Count>=16)throw Invalid("JSON array limit.");
                        values.Add(Read(element,depth+1));if(Take(']'))break;Need(',');
                    }
                    Array array=Array.CreateInstance(element,values.Count);for(int i=0;i<values.Count;i++)array.SetValue(values[i],i);return array;
                }
                Need('{');object result=Activator.CreateInstance(type);
                var fields=type.GetFields(BindingFlags.Instance|BindingFlags.Public);var seen=new HashSet<string>(StringComparer.Ordinal);
                if(!Take('}'))while(true)
                {
                    string key=String();FieldInfo field=Array.Find(fields,f=>f.Name==key);
                    if(field==null || !seen.Add(key))throw Invalid("Unknown or duplicate field: "+key);
                    Need(':');field.SetValue(result,Read(field.FieldType,depth+1));if(Take('}'))break;Need(',');
                }
                if(seen.Count!=fields.Length)throw Invalid("All v4 DTO fields must be present: "+type.Name);
                return result;
            }
            private string String()
            {
                Need('"');var value=new StringBuilder();
                while(pos<text.Length)
                {
                    char c=text[pos++];if(c=='"')return value.ToString();
                    if(c<' ')throw Invalid("Unescaped control character.");
                    if(c=='\\')
                    {
                        if(pos>=text.Length)throw Invalid("Incomplete escape.");c=text[pos++];
                        switch(c)
                        {
                            case '"':case '\\':case '/':break;
                            case 'b':c='\b';break;case 'f':c='\f';break;case 'n':c='\n';break;case 'r':c='\r';break;case 't':c='\t';break;
                            case 'u':
                                if(pos+4>text.Length || !ushort.TryParse(text.Substring(pos,4),NumberStyles.AllowHexSpecifier,CultureInfo.InvariantCulture,out ushort code))throw Invalid("Invalid Unicode escape.");
                                pos+=4;c=(char)code;break;
                            default:throw Invalid("Invalid JSON escape.");
                        }
                    }
                    value.Append(c);if(value.Length>128)throw Invalid("JSON string limit.");
                }
                throw Invalid("Unterminated string.");
            }
        }
    }
}
