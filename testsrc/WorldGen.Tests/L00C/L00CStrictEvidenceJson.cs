#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ISRWorldGen.L00C.Laboratory;

// Tiny strict flat-object parser for attestations. Duplicate keys, unknown
// fields, trailing bytes, fractions/exponents and non-JSON whitespace refuse.
internal sealed class L00CStrictEvidenceJson
{
    private readonly Dictionary<string, Item> values = new(StringComparer.Ordinal);
    internal static L00CStrictEvidenceJson Parse(string text)
    {
        var r = new L00CStrictEvidenceJson(); int i = 0; Ws(text, ref i); Need(text, ref i, '{'); Ws(text, ref i);
        if (Take(text, ref i, '}')) { End(text, i); return r; }
        while (true) { string key = String(text, ref i); Ws(text, ref i); Need(text, ref i, ':'); Ws(text, ref i); if (r.values.ContainsKey(key)) throw new InvalidOperationException("L00-C evidence duplicate property."); r.values.Add(key, Value(text, ref i)); Ws(text, ref i); if (Take(text, ref i, '}')) break; Need(text, ref i, ','); Ws(text, ref i); }
        End(text, i); return r;
    }
    internal void Exactly(params string[] names) { if (values.Count != names.Length) throw new InvalidOperationException("L00-C evidence property count mismatch."); foreach (string n in names) if (!values.ContainsKey(n)) throw new InvalidOperationException("L00-C evidence property absent."); }
    internal string StringValue(string name) => NeedValue(name, Kind.String).Text;
    internal bool True(string name) => NeedValue(name, Kind.Bool).Text == "true";
    internal bool Bool(string name) => NeedValue(name, Kind.Bool).Text == "true";
    internal int Int(string name) { string v = NeedValue(name, Kind.Number).Text; if (!int.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out int n) || n < 0) throw new InvalidOperationException("L00-C evidence integer invalid."); return n; }
    private Item NeedValue(string name, Kind kind) { if (!values.TryGetValue(name, out Item value) || value.Kind != kind) throw new InvalidOperationException("L00-C evidence type invalid."); return value; }
    private static Item Value(string t, ref int i) { if (i >= t.Length) throw new InvalidOperationException("L00-C evidence truncated."); if (t[i] == '"') return new Item(Kind.String, String(t, ref i)); if (Word(t, ref i, "true")) return new Item(Kind.Bool, "true"); if (Word(t, ref i, "false")) return new Item(Kind.Bool, "false"); int s=i; if(t[i]=='-') i++; if(i>=t.Length || t[i]<'0'||t[i]>'9') throw new InvalidOperationException("L00-C evidence value invalid."); if(t[i]=='0') i++; else while(i<t.Length&&t[i]>='0'&&t[i]<='9')i++; if(i<t.Length&&(t[i]=='.'||t[i]=='e'||t[i]=='E')) throw new InvalidOperationException("L00-C evidence integer invalid."); return new Item(Kind.Number,t.Substring(s,i-s)); }
    private static string String(string t, ref int i) { Need(t,ref i,'"'); var b=new StringBuilder(); while(i<t.Length){char c=t[i++];if(c=='"')return b.ToString();if(c<0x20)throw new InvalidOperationException("L00-C evidence string invalid.");if(c!='\\'){b.Append(c);continue;}if(i>=t.Length)break;char e=t[i++];if(e=='"'||e=='\\'||e=='/')b.Append(e);else if(e=='b')b.Append('\b');else if(e=='f')b.Append('\f');else if(e=='n')b.Append('\n');else if(e=='r')b.Append('\r');else if(e=='t')b.Append('\t');else throw new InvalidOperationException("L00-C evidence escape invalid.");}throw new InvalidOperationException("L00-C evidence string truncated."); }
    private static void Ws(string t,ref int i){while(i<t.Length&&(t[i]==' '||t[i]=='\t'||t[i]=='\r'||t[i]=='\n'))i++;} private static void Need(string t,ref int i,char c){if(i>=t.Length||t[i++]!=c)throw new InvalidOperationException("L00-C evidence malformed.");} private static bool Take(string t,ref int i,char c){if(i<t.Length&&t[i]==c){i++;return true;}return false;} private static bool Word(string t,ref int i,string w){if(i+w.Length>t.Length||string.CompareOrdinal(t,i,w,0,w.Length)!=0)return false;i+=w.Length;return true;} private static void End(string t,int i){Ws(t,ref i);if(i!=t.Length)throw new InvalidOperationException("L00-C evidence trailing bytes.");}
    private enum Kind { String, Bool, Number } private readonly struct Item { internal Item(Kind k,string t){Kind=k;Text=t;}internal Kind Kind{get;}internal string Text{get;} }
}
