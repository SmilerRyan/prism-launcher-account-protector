#region Using directives
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.IO;
    using System.Runtime.InteropServices;
    using System.Security.Cryptography;
    using System.Text;
    using System.Threading;
#endregion

namespace MyApp {

    // ---------------------------------------------------------------
    //  Minimal ordered JSON DOM (parse + write, no external deps)
    // ---------------------------------------------------------------
    sealed class JsonObject {

        readonly List<string> keys = new List<string>();
        readonly Dictionary<string, object> map =
            new Dictionary<string, object>(StringComparer.Ordinal);

        public int Count { get { return keys.Count; } }
        public List<string> Keys { get { return keys; } }

        public bool Has(string k) { return map.ContainsKey(k); }

        public object Get(string k) {
            object v;
            return map.TryGetValue(k, out v) ? v : null;
        }

        public void Set(string k, object v) {
            if (!map.ContainsKey(k)) keys.Add(k);
            map[k] = v;
        }
    }

    static class Json {

        // ---------------------------- parse ----------------------------

        public static object Parse(string s) {
            int i = 0;
            object v = ParseValue(s, ref i);
            SkipWs(s, ref i);
            if (i < s.Length) throw new FormatException("Trailing content at offset " + i);
            return v;
        }

        static void SkipWs(string s, ref int i) {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\t' ||
                                    s[i] == '\r' || s[i] == '\n')) i++;
        }

        static void Expect(string s, ref int i, string word) {
            if (i + word.Length > s.Length ||
                string.CompareOrdinal(s, i, word, 0, word.Length) != 0)
                throw new FormatException("Expected '" + word + "' at offset " + i);
            i += word.Length;
        }

        static object ParseValue(string s, ref int i) {
            SkipWs(s, ref i);
            if (i >= s.Length) throw new FormatException("Unexpected end of JSON.");
            char c = s[i];
            switch (c) {
                case '{': return ParseObject(s, ref i);
                case '[': return ParseArray(s, ref i);
                case '"': return ParseString(s, ref i);
                case 't': Expect(s, ref i, "true");  return true;
                case 'f': Expect(s, ref i, "false"); return false;
                case 'n': Expect(s, ref i, "null");  return null;
                default:  return ParseNumber(s, ref i);
            }
        }

        static JsonObject ParseObject(string s, ref int i) {
            JsonObject o = new JsonObject();
            i++; // consume '{'
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return o; }
            while (true) {
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != '"')
                    throw new FormatException("Expected string key at offset " + i);
                string k = ParseString(s, ref i);
                SkipWs(s, ref i);
                if (i >= s.Length || s[i] != ':')
                    throw new FormatException("Expected ':' at offset " + i);
                i++;
                o.Set(k, ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == '}') { i++; return o; }
                throw new FormatException("Expected ',' or '}' at offset " + i);
            }
        }

        static List<object> ParseArray(string s, ref int i) {
            List<object> a = new List<object>();
            i++; // consume '['
            SkipWs(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return a; }
            while (true) {
                a.Add(ParseValue(s, ref i));
                SkipWs(s, ref i);
                if (i < s.Length && s[i] == ',') { i++; continue; }
                if (i < s.Length && s[i] == ']') { i++; return a; }
                throw new FormatException("Expected ',' or ']' at offset " + i);
            }
        }

        static string ParseString(string s, ref int i) {
            StringBuilder sb = new StringBuilder();
            i++; // consume opening quote
            while (true) {
                if (i >= s.Length) throw new FormatException("Unterminated string.");
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                if (i >= s.Length) throw new FormatException("Unterminated escape.");
                char e = s[i++];
                switch (e) {
                    case '"':  sb.Append('"');  break;
                    case '\\': sb.Append('\\'); break;
                    case '/':  sb.Append('/');  break;
                    case 'b':  sb.Append('\b'); break;
                    case 'f':  sb.Append('\f'); break;
                    case 'n':  sb.Append('\n'); break;
                    case 'r':  sb.Append('\r'); break;
                    case 't':  sb.Append('\t'); break;
                    case 'u':
                        if (i + 4 > s.Length) throw new FormatException("Bad \\u escape.");
                        sb.Append((char)ushort.Parse(s.Substring(i, 4),
                            NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                        i += 4;
                        break;
                    default:
                        throw new FormatException("Bad escape \\" + e);
                }
            }
        }

        static object ParseNumber(string s, ref int i) {
            int start = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            string t = s.Substring(start, i - start);
            if (t.Length == 0) throw new FormatException("Invalid token at offset " + start);
            if (t.IndexOf('.') < 0 && t.IndexOf('e') < 0 && t.IndexOf('E') < 0) {
                long l;
                if (long.TryParse(t, NumberStyles.Integer,
                                  CultureInfo.InvariantCulture, out l)) return l;
            }
            double d;
            if (double.TryParse(t, NumberStyles.Float,
                                CultureInfo.InvariantCulture, out d)) return d;
            throw new FormatException("Invalid number: " + t);
        }

        // ---------------------------- write ----------------------------

        public static string Write(object v, bool pretty) {
            StringBuilder sb = new StringBuilder();
            WriteValue(sb, v, pretty, 0);
            return sb.ToString();
        }

        static void Indent(StringBuilder sb, bool pretty, int depth) {
            if (!pretty) return;
            sb.Append('\n');
            sb.Append(' ', depth * 2);
        }

        static void WriteValue(StringBuilder sb, object v, bool pretty, int depth) {
            if (v == null) { sb.Append("null"); return; }

            JsonObject o = v as JsonObject;
            if (o != null) { WriteObject(sb, o, pretty, depth); return; }

            List<object> a = v as List<object>;
            if (a != null) { WriteArray(sb, a, pretty, depth); return; }

            if (v is bool)   { sb.Append(((bool)v) ? "true" : "false"); return; }
            if (v is string) { WriteString(sb, (string)v); return; }
            if (v is long)   { sb.Append(((long)v).ToString(CultureInfo.InvariantCulture)); return; }
            if (v is int)    { sb.Append(((int)v).ToString(CultureInfo.InvariantCulture)); return; }

            if (v is double) {
                double d = (double)v;
                if (!double.IsNaN(d) && !double.IsInfinity(d) &&
                    d == Math.Floor(d) && Math.Abs(d) < 1e15)
                    sb.Append(((long)d).ToString(CultureInfo.InvariantCulture));
                else
                    sb.Append(d.ToString("R", CultureInfo.InvariantCulture));
                return;
            }

            WriteString(sb, v.ToString());
        }

        static void WriteObject(StringBuilder sb, JsonObject o, bool pretty, int depth) {
            if (o.Count == 0) { sb.Append("{}"); return; }
            sb.Append('{');
            for (int n = 0; n < o.Keys.Count; n++) {
                if (n > 0) sb.Append(',');
                Indent(sb, pretty, depth + 1);
                WriteString(sb, o.Keys[n]);
                sb.Append(':');
                if (pretty) sb.Append(' ');
                WriteValue(sb, o.Get(o.Keys[n]), pretty, depth + 1);
            }
            Indent(sb, pretty, depth);
            sb.Append('}');
        }

        static void WriteArray(StringBuilder sb, List<object> a, bool pretty, int depth) {
            if (a.Count == 0) { sb.Append("[]"); return; }
            sb.Append('[');
            for (int n = 0; n < a.Count; n++) {
                if (n > 0) sb.Append(',');
                Indent(sb, pretty, depth + 1);
                WriteValue(sb, a[n], pretty, depth + 1);
            }
            Indent(sb, pretty, depth);
            sb.Append(']');
        }

        static void WriteString(StringBuilder sb, string s) {
            sb.Append('"');
            for (int n = 0; n < s.Length; n++) {
                char c = s[n];
                switch (c) {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b");  break;
                    case '\f': sb.Append("\\f");  break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else         sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }

    // ---------------------------------------------------------------
    //  DPAPI wrapper using P/Invoke (replaces ProtectedData)
    // ---------------------------------------------------------------
    static class DPAPI {

        [StructLayout(LayoutKind.Sequential)]
        struct DATA_BLOB {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CryptProtectData(ref DATA_BLOB pDataIn,
            string szDataDescription, ref DATA_BLOB pOptionalEntropy,
            IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

        [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn,
            out string szDataDescription, ref DATA_BLOB pOptionalEntropy,
            IntPtr pvReserved, IntPtr pPromptStruct, int dwFlags, ref DATA_BLOB pDataOut);

        const int CRYPTPROTECT_UI_FORBIDDEN = 0x1;
        const int CRYPTPROTECT_LOCAL_MACHINE = 0x4;

        public static byte[] Protect(byte[] plaintext, byte[] entropy) {
            DATA_BLOB inp = AllocBLOB(plaintext);
            DATA_BLOB opt = AllocBLOB(entropy);
            DATA_BLOB outp = default(DATA_BLOB);

            bool ok = CryptProtectData(ref inp, null, ref opt, IntPtr.Zero, IntPtr.Zero,
                                       0 /* no extra flags */, ref outp);
            FreeBLOB(inp); FreeBLOB(opt);
            if (!ok) throw new CryptographicException("CryptProtectData failed: " + Marshal.GetLastWin32Error());

            byte[] result = new byte[outp.cbData];
            Marshal.Copy(outp.pbData, result, 0, outp.cbData);
            FreeBLOB(outp);
            return result;
        }

        public static byte[] Unprotect(byte[] ciphertext, byte[] entropy) {
            DATA_BLOB inp = AllocBLOB(ciphertext);
            DATA_BLOB opt = AllocBLOB(entropy);
            DATA_BLOB outp = default(DATA_BLOB);
            string desc;

            bool ok = CryptUnprotectData(ref inp, out desc, ref opt, IntPtr.Zero, IntPtr.Zero,
                                         0 /* no extra flags */, ref outp);
            FreeBLOB(inp); FreeBLOB(opt);
            if (!ok) throw new CryptographicException("CryptUnprotectData failed: " + Marshal.GetLastWin32Error());

            byte[] result = new byte[outp.cbData];
            Marshal.Copy(outp.pbData, result, 0, outp.cbData);
            FreeBLOB(outp);
            return result;
        }

        static DATA_BLOB AllocBLOB(byte[] data) {
            IntPtr ptr = Marshal.AllocHGlobal(data.Length);
            Marshal.Copy(data, 0, ptr, data.Length);
            return new DATA_BLOB { cbData = data.Length, pbData = ptr };
        }

        static void FreeBLOB(DATA_BLOB b) {
            if (b.pbData != IntPtr.Zero) Marshal.FreeHGlobal(b.pbData);
        }
    }

    // ---------------------------------------------------------------
    //  Console app
    // ---------------------------------------------------------------
    class Program {

        const string AccountsFileName = "accounts.json";
        const string EncryptedType    = "encrypted";

        // Build absolute path: go up 2 directories from exe to find launcher in parent folder
        static readonly string LauncherPath = 
            Path.Combine(
                Path.GetDirectoryName(
                    Path.GetDirectoryName(AppDomain.CurrentDomain.BaseDirectory)
                ) ?? AppDomain.CurrentDomain.BaseDirectory,
                "prismlauncher.exe");

        const string ProcessName   = "prismlauncher";   // prismlauncher.exe
        const string EntropyString = "prism-msa-v1";

        public static void Main() {

            string here = AppDomain.CurrentDomain.BaseDirectory;
            Directory.SetCurrentDirectory(here);

            string jsonPath = Path.Combine(here, AccountsFileName);

            if (!File.Exists(jsonPath)) {
                Console.WriteLine(AccountsFileName + " not found in " + here);
                Pause();
                Environment.Exit(1);
            }

            // --- 1. UNLOCK ---
            try {
                Unlock(jsonPath);
            }
            catch (Exception ex) {
                Console.WriteLine("Unlock failed - a blob wasn't created by this user on this machine.");
                Console.WriteLine(ex.Message);
                Pause();
                Environment.Exit(1);
            }

            // --- 2. Launch Prism ---
            if (!File.Exists(LauncherPath)) {
                Console.WriteLine("Launcher not found: " + LauncherPath);
                Pause();
                Environment.Exit(1);
            }

            ProcessStartInfo psi = new ProcessStartInfo(LauncherPath);
            psi.UseShellExecute = true;
            psi.WorkingDirectory = Path.GetDirectoryName(LauncherPath);
            Process.Start(psi);

            // --- 3. Wait for process to appear (max 60s) ---
            int t = 0;
            while (!IsRunning()) {
                t++;
                if (t >= 30) break;
                Thread.Sleep(2000);
            }

            // --- 4. Wait for it to close ---
            while (IsRunning()) Thread.Sleep(2000);
            Thread.Sleep(2000);

            // --- 5. LOCK ---
            try {
                Lock(jsonPath);
            }
            catch (Exception ex) {
                Console.WriteLine("Lock failed: " + ex.Message);
                Pause();
                Environment.Exit(1);
            }

            Environment.Exit(0);
        }

        // ---------------------------------------------------------------
        //  A locked account looks like:  { "type": "encrypted", "data": "..." }
        // ---------------------------------------------------------------
        static string ReadBlob(JsonObject acct) {

            string type = acct.Get("type") as string;
            if (type != EncryptedType) return null;

            return acct.Get("data") as string;
        }

        static void Unlock(string path) {
            byte[] entropy = Encoding.UTF8.GetBytes(EntropyString);

            JsonObject root = (JsonObject)Json.Parse(File.ReadAllText(path));

            List<object> accounts = root.Get("accounts") as List<object>;
            if (accounts == null) return;

            List<object> outp = new List<object>();

            foreach (object item in accounts) {
                JsonObject acct = item as JsonObject;
                if (acct == null) { outp.Add(item); continue; }

                string blob = ReadBlob(acct);

                if (!string.IsNullOrEmpty(blob)) {
                    byte[] plain = DPAPI.Unprotect(
                        Convert.FromBase64String(blob), entropy);
                    outp.Add(Json.Parse(Encoding.UTF8.GetString(plain)));
                }
                else {
                    outp.Add(acct);
                }
            }

            root.Set("accounts", outp);
            Save(path, root);
        }

        static void Lock(string path) {
            byte[] entropy = Encoding.UTF8.GetBytes(EntropyString);

            JsonObject root = (JsonObject)Json.Parse(File.ReadAllText(path));

            List<object> accounts = root.Get("accounts") as List<object>;
            if (accounts == null) return;

            List<object> outp = new List<object>();

            foreach (object item in accounts) {
                JsonObject acct = item as JsonObject;
                if (acct == null) { outp.Add(item); continue; }

                string type = acct.Get("type") as string;

                // Fold every non-Offline, non-already-encrypted account into
                // the { "type": "encrypted", "data": ... } envelope.
                if (type != "Offline" && type != EncryptedType) {

                    byte[] plain = Encoding.UTF8.GetBytes(Json.Write(acct, false));
                    string blob = Convert.ToBase64String(DPAPI.Protect(plain, entropy));

                    JsonObject envelope = new JsonObject();
                    envelope.Set("type", EncryptedType);
                    envelope.Set("data", blob);

                    outp.Add(envelope);
                }
                else {
                    outp.Add(acct);
                }
            }

            root.Set("accounts", outp);
            Save(path, root);
        }

        // ---------------------------------------------------------------

        static void Save(string path, JsonObject root) {
            File.WriteAllText(path, Json.Write(root, true), new UTF8Encoding(false));
        }

        static bool IsRunning() {
            Process[] ps = Process.GetProcessesByName(ProcessName);
            try {
                return ps.Length > 0;
            }
            finally {
                for (int i = 0; i < ps.Length; i++) ps[i].Dispose();
            }
        }

        static void Pause() {
            try {
                Console.Write("Press any key to continue . . .");
                Console.ReadKey(true);
                Console.WriteLine();
            }
            catch {
                // stdin redirected / no console attached - just carry on
            }
        }
    }
}