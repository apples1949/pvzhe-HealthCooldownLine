using System;
using System.IO;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

class Program
{
    static void Main(string[] args)
    {
        string path = args[0];
        string pat = args.Length > 1 ? args[1] : null;
        bool verbose = args.Length > 2 && args[2] == "-v";
        // 新增：字段搜索模式 —— `mdprobe <dll> -f <字段名>` 列出所有含该字段的类型（跨继承链定位数据源）
        string fieldSearch = null;
        if (args.Length > 2 && args[1] == "-f") { fieldSearch = args[2]; }
        using var fs = File.OpenRead(path);
        using var pe = new PEReader(fs);
        var md = pe.GetMetadataReader();
        foreach (var h in md.TypeDefinitions)
        {
            var td = md.GetTypeDefinition(h);
            string ns = md.GetString(td.Namespace);
            string nm = md.GetString(td.Name);
            string full = string.IsNullOrEmpty(ns) ? nm : ns + "." + nm;
            if (fieldSearch != null)
            {
                bool hit = false;
                foreach (var fh0 in td.GetFields())
                {
                    var fd0 = md.GetFieldDefinition(fh0);
                    if (md.GetString(fd0.Name).IndexOf(fieldSearch, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        hit = true;
                        break;
                    }
                }
                if (!hit) continue;
            }
            else if (pat != null && full.IndexOf(pat, StringComparison.OrdinalIgnoreCase) < 0) continue;
            string baseT = "";
            if (!td.BaseType.IsNil)
            {
                try { baseT = " : " + TypeName(md, td.BaseType); } catch { }
            }
            Console.WriteLine("TYPE " + full + baseT);
            foreach (var fh in td.GetFields())
            {
                var fd = md.GetFieldDefinition(fh);
                string ft = "";
                try { ft = " : " + string.Join("", fd.DecodeSignature(new SigProv(md), null)); } catch { }
                bool isStatic = (fd.Attributes & System.Reflection.FieldAttributes.Static) != 0;
                Console.WriteLine("    F  " + md.GetString(fd.Name) + ft + (isStatic ? "  [static]" : ""));
            }
            foreach (var mh in td.GetMethods())
            {
                var mdd = md.GetMethodDefinition(mh);
                string mn = md.GetString(mdd.Name);
                if (!verbose && (mn.StartsWith("get_") || mn.StartsWith("set_") || mn.StartsWith("add_") || mn.StartsWith("remove_"))) continue;
                int pc = -1;
                try {
                    var br = md.GetBlobReader(mdd.Signature);
                    br.ReadByte();
                    pc = br.ReadCompressedInteger();
                } catch { }
                Console.WriteLine("    M  " + mn + "/" + pc);
            }
            foreach (var ph in td.GetProperties())
            {
                var pd = md.GetPropertyDefinition(ph);
                Console.WriteLine("    P  " + md.GetString(pd.Name));
            }
        }
    }

    static string TypeName(MetadataReader md, EntityHandle h)
    {
        switch (h.Kind)
        {
            case HandleKind.TypeReference:
                var tr = md.GetTypeReference((TypeReferenceHandle)h);
                return md.GetString(tr.Name);
            case HandleKind.TypeDefinition:
                var td2 = md.GetTypeDefinition((TypeDefinitionHandle)h);
                return md.GetString(td2.Name);
            default:
                return h.Kind.ToString();
        }
    }

    sealed class SigProv : ISignatureTypeProvider<string, object>
    {
        private readonly MetadataReader _md;
        public SigProv(MetadataReader md) { _md = md; }
        private string Nm(EntityHandle h)
        {
            try
            {
                if (h.Kind == HandleKind.TypeReference)
                {
                    var tr = _md.GetTypeReference((TypeReferenceHandle)h);
                    return _md.GetString(tr.Name);
                }
                if (h.Kind == HandleKind.TypeDefinition)
                {
                    var td = _md.GetTypeDefinition((TypeDefinitionHandle)h);
                    return _md.GetString(td.Name);
                }
            }
            catch { }
            return "?";
        }
        public string GetPrimitiveType(PrimitiveTypeCode t) => t.ToString();
        public string GetTypeFromDefinition(MetadataReader r, TypeDefinitionHandle h, byte k) => Nm(h);
        public string GetTypeFromReference(MetadataReader r, TypeReferenceHandle h, byte k) => Nm(h);
        public string GetTypeFromSpecification(MetadataReader r, object c, TypeSpecificationHandle h, byte k) => "spec";
        public string GetSZArrayType(string e) => e + "[]";
        public string GetArrayType(string e, System.Reflection.Metadata.ArrayShape s) => e + "[,]";
        public string GetByReferenceType(string e) => "ref " + e;
        public string GetPointerType(string e) => e + "*";
        public string GetGenericInstantiation(string g, System.Collections.Immutable.ImmutableArray<string> args)
            => g + "<" + string.Join(",", args) + ">";
        public string GetGenericMethodParameter(object c, int i) => "!!" + i;
        public string GetGenericTypeParameter(object c, int i) => "!" + i;
        public string GetFunctionPointerType(MethodSignature<string> s) => "fnptr";
        public string GetModifiedType(string m, string u, bool req) => u;
        public string GetPinnedType(string e) => e;
    }
}
