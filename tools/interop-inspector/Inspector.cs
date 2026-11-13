using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

class P : ISignatureTypeProvider<string, object?>
{
    readonly MetadataReader md;
    public P(MetadataReader m) { md = m; }
    public string GetPrimitiveType(PrimitiveTypeCode c) => c.ToString();
    public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte raw)
    { var t = r.GetTypeDefinition(h); var n = r.GetString(t.Name); var ns = r.GetString(t.Namespace); return ns.Length > 0 ? ns + "." + n : n; }
    public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte raw)
    { var t = r.GetTypeReference(h); var n = r.GetString(t.Name); var ns = r.GetString(t.Namespace); return ns.Length > 0 ? ns + "." + n : n; }
    public string GetTypeFromSpecification(MetadataReader r, object? c, TypeSpecificationHandle h, byte raw)
        => r.GetTypeSpecification(h).DecodeSignature(this, c);
    public string GetSZArrayType(string e) => e + "[]";
    public string GetArrayType(string e, ArrayShape s) => e + "[" + new string(',', s.Rank - 1) + "]";
    public string GetByReferenceType(string e) => "ref " + e;
    public string GetPointerType(string e) => e + "*";
    public string GetGenericInstantiation(string g, ImmutableArray<string> a) => g + "<" + string.Join(",", a) + ">";
    public string GetGenericMethodParameter(object? c, int i) => "!!" + i;
    public string GetGenericTypeParameter(object? c, int i) => "!" + i;
    public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr";
    public string GetModifiedType(string m, string u, bool req) => u;
    public string GetPinnedType(string e) => e;
}

class Program
{
    static void Main(string[] args)
    {
        var path = args[0];
        var mode = args[1];
        var keys = args.Skip(2).ToArray();
        using var fs = File.OpenRead(path);
        using var pe = new PEReader(fs);
        var md = pe.GetMetadataReader();
        var prov = new P(md);

        foreach (var th in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(th);
            var nm = md.GetString(td.Name);
            var ns = md.GetString(td.Namespace);
            var full = ns.Length > 0 ? ns + "." + nm : nm;

            if (mode == "find")
            {
                var hits = new List<string>();
                foreach (var ph in td.GetProperties())
                {
                    var pd = md.GetPropertyDefinition(ph);
                    var pn = md.GetString(pd.Name);
                    if (pn.Contains("k__BackingField")) continue;
                    if (!keys.Any(k => pn.Contains(k, StringComparison.OrdinalIgnoreCase))) continue;
                    string t; try { t = pd.DecodeSignature(prov, null).ReturnType; } catch { t = "?"; }
                    hits.Add("    " + pn + " : " + t);
                }
                if (hits.Count > 0) { Console.WriteLine("### " + full); hits.ForEach(Console.WriteLine); }
                continue;
            }

            if (keys.Length > 0 && !keys.Any(k => full.Contains(k, StringComparison.OrdinalIgnoreCase))) continue;

            if (mode == "list") { Console.WriteLine(full); continue; }

            string baseName = "";
            try
            {
                var bt = td.BaseType;
                if (!bt.IsNil)
                    baseName = bt.Kind == HandleKind.TypeReference
                        ? prov.GetTypeFromReference(md, (TypeReferenceHandle)bt, 0)
                        : prov.GetTypeFromDefinition(md, (TypeDefinitionHandle)bt, 0);
            }
            catch { }
            Console.WriteLine("### " + full + (baseName.Length > 0 ? "  : " + baseName : ""));

            foreach (var ph in td.GetProperties())
            {
                var pd = md.GetPropertyDefinition(ph);
                var pn = md.GetString(pd.Name);
                string t;
                try { t = pd.DecodeSignature(prov, null).ReturnType; } catch { t = "?"; }
                bool isStatic = false;
                var acc = pd.GetAccessors();
                foreach (var mh in new[] { acc.Getter, acc.Setter })
                {
                    if (mh.IsNil) continue;
                    var m = md.GetMethodDefinition(mh);
                    if ((m.Attributes & System.Reflection.MethodAttributes.Static) != 0) isStatic = true;
                }
                var acc2 = pd.GetAccessors();
                string kw = (acc2.Getter.IsNil ? "" : "get") + (acc2.Setter.IsNil ? "" : (acc2.Getter.IsNil ? "set" : ",set"));
                Console.WriteLine("    prop " + pn + " : " + t + (isStatic ? "  [static]" : "") + "  [" + kw + "]");
            }
            foreach (var fh in td.GetFields())
            {
                var fd = md.GetFieldDefinition(fh);
                var fn = md.GetString(fd.Name);
                if (fn.StartsWith("NativeFieldInfoPtr_")) { Console.WriteLine("    field " + fn.Substring(19) + " : <il2cpp>"); continue; }
                string t;
                try { t = fd.DecodeSignature(prov, null); } catch { t = "?"; }
                Console.WriteLine("    field " + fn + " : " + t);
            }
        }
    }
}
