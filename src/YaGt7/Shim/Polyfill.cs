// Polyfill per .NET Framework 4.8: tipi e metodi che il codice comune (scritto per .NET 10) usa e che il Framework non ha.
using System;
using System.Collections.Generic;

namespace YaGt.Core
{
    /// <summary>System.Math con in più Clamp (assente in .NET Framework). Ha la precedenza su System.Math per tutto
    /// il codice nello spazio dei nomi YaGt.Core.</summary>
    public static class Math
    {
        public const double PI = System.Math.PI;
        public const double E = System.Math.E;
        public static int Max(int a, int b) => System.Math.Max(a, b);
        public static int Min(int a, int b) => System.Math.Min(a, b);
        public static int Clamp(int v, int lo, int hi) => v < lo ? lo : v > hi ? hi : v;
        public static long Max(long a, long b) => System.Math.Max(a, b);
        public static long Min(long a, long b) => System.Math.Min(a, b);
        public static long Clamp(long v, long lo, long hi) => v < lo ? lo : v > hi ? hi : v;
        public static double Max(double a, double b) => System.Math.Max(a, b);
        public static double Min(double a, double b) => System.Math.Min(a, b);
        public static double Clamp(double v, double lo, double hi) => v < lo ? lo : v > hi ? hi : v;
        public static float Max(float a, float b) => System.Math.Max(a, b);
        public static float Min(float a, float b) => System.Math.Min(a, b);
        public static float Clamp(float v, float lo, float hi) => v < lo ? lo : v > hi ? hi : v;
        public static decimal Max(decimal a, decimal b) => System.Math.Max(a, b);
        public static decimal Min(decimal a, decimal b) => System.Math.Min(a, b);
        public static decimal Clamp(decimal v, decimal lo, decimal hi) => v < lo ? lo : v > hi ? hi : v;
        public static byte Max(byte a, byte b) => System.Math.Max(a, b);
        public static byte Min(byte a, byte b) => System.Math.Min(a, b);
        public static byte Clamp(byte v, byte lo, byte hi) => v < lo ? lo : v > hi ? hi : v;
        public static short Max(short a, short b) => System.Math.Max(a, b);
        public static short Min(short a, short b) => System.Math.Min(a, b);
        public static short Clamp(short v, short lo, short hi) => v < lo ? lo : v > hi ? hi : v;
        public static uint Max(uint a, uint b) => System.Math.Max(a, b);
        public static uint Min(uint a, uint b) => System.Math.Min(a, b);
        public static uint Clamp(uint v, uint lo, uint hi) => v < lo ? lo : v > hi ? hi : v;
        public static ulong Max(ulong a, ulong b) => System.Math.Max(a, b);
        public static ulong Min(ulong a, ulong b) => System.Math.Min(a, b);
        public static ulong Clamp(ulong v, ulong lo, ulong hi) => v < lo ? lo : v > hi ? hi : v;
        public static sbyte Max(sbyte a, sbyte b) => System.Math.Max(a, b);
        public static sbyte Min(sbyte a, sbyte b) => System.Math.Min(a, b);
        public static sbyte Clamp(sbyte v, sbyte lo, sbyte hi) => v < lo ? lo : v > hi ? hi : v;
        public static ushort Max(ushort a, ushort b) => System.Math.Max(a, b);
        public static ushort Min(ushort a, ushort b) => System.Math.Min(a, b);
        public static ushort Clamp(ushort v, ushort lo, ushort hi) => v < lo ? lo : v > hi ? hi : v;
        public static int Abs(int a) => System.Math.Abs(a);
        public static int Sign(int a) => System.Math.Sign(a);
        public static long Abs(long a) => System.Math.Abs(a);
        public static int Sign(long a) => System.Math.Sign(a);
        public static double Abs(double a) => System.Math.Abs(a);
        public static int Sign(double a) => System.Math.Sign(a);
        public static float Abs(float a) => System.Math.Abs(a);
        public static int Sign(float a) => System.Math.Sign(a);
        public static decimal Abs(decimal a) => System.Math.Abs(a);
        public static int Sign(decimal a) => System.Math.Sign(a);
        public static short Abs(short a) => System.Math.Abs(a);
        public static int Sign(short a) => System.Math.Sign(a);
        public static sbyte Abs(sbyte a) => System.Math.Abs(a);
        public static int Sign(sbyte a) => System.Math.Sign(a);
        public static double Acos(double a) => System.Math.Acos(a);
        public static double Asin(double a) => System.Math.Asin(a);
        public static double Atan(double a) => System.Math.Atan(a);
        public static double Cos(double a) => System.Math.Cos(a);
        public static double Cosh(double a) => System.Math.Cosh(a);
        public static double Sin(double a) => System.Math.Sin(a);
        public static double Sinh(double a) => System.Math.Sinh(a);
        public static double Tan(double a) => System.Math.Tan(a);
        public static double Tanh(double a) => System.Math.Tanh(a);
        public static double Exp(double a) => System.Math.Exp(a);
        public static double Log10(double a) => System.Math.Log10(a);
        public static double Sqrt(double a) => System.Math.Sqrt(a);
        public static double Atan2(double y, double x) => System.Math.Atan2(y, x);
        public static double Log(double a) => System.Math.Log(a);
        public static double Log(double a, double b) => System.Math.Log(a, b);
        public static double Log2(double a) => System.Math.Log(a) / System.Math.Log(2);
        public static double Pow(double a, double b) => System.Math.Pow(a, b);
        public static double Cbrt(double a) => a < 0 ? -System.Math.Pow(-a, 1.0 / 3) : System.Math.Pow(a, 1.0 / 3);
        public static double IEEERemainder(double a, double b) => System.Math.IEEERemainder(a, b);
        public static double Ceiling(double a) => System.Math.Ceiling(a);
        public static decimal Ceiling(decimal a) => System.Math.Ceiling(a);
        public static double Floor(double a) => System.Math.Floor(a);
        public static decimal Floor(decimal a) => System.Math.Floor(a);
        public static double Truncate(double a) => System.Math.Truncate(a);
        public static decimal Truncate(decimal a) => System.Math.Truncate(a);
        public static double Round(double a) => System.Math.Round(a);
        public static double Round(double a, int d) => System.Math.Round(a, d);
        public static double Round(double a, MidpointRounding m) => System.Math.Round(a, m);
        public static double Round(double a, int d, MidpointRounding m) => System.Math.Round(a, d, m);
        public static decimal Round(decimal a) => System.Math.Round(a);
        public static decimal Round(decimal a, int d) => System.Math.Round(a, d);
        public static decimal Round(decimal a, MidpointRounding m) => System.Math.Round(a, m);
        public static decimal Round(decimal a, int d, MidpointRounding m) => System.Math.Round(a, d, m);
        public static long BigMul(int a, int b) => System.Math.BigMul(a, b);
        public static int DivRem(int a, int b, out int r) => System.Math.DivRem(a, b, out r);
        public static double Hypot(double a, double b) => System.Math.Sqrt(a * a + b * b);
    }

    public static class PolyfillExtensions
    {
        public static bool Contains(this string s, char c) => s.IndexOf(c) >= 0;
        public static bool Contains(this string s, string v, StringComparison cmp) => s.IndexOf(v, cmp) >= 0;
        public static bool StartsWith(this string s, char c) => s.Length > 0 && s[0] == c;
        public static bool EndsWith(this string s, char c) => s.Length > 0 && s[s.Length - 1] == c;
        public static string Replace(this string s, string a, string b, StringComparison cmp)
        {
            if (string.IsNullOrEmpty(a)) return s;
            var sb = new System.Text.StringBuilder();
            int i = 0, j;
            while ((j = s.IndexOf(a, i, cmp)) >= 0) { sb.Append(s, i, j - i).Append(b); i = j + a.Length; }
            return sb.Append(s, i, s.Length - i).ToString();
        }
        public static string[] Split(this string s, char c, StringSplitOptions o) => s.Split(new[] { c }, o);
        public static string[] Split(this string s, char c, int count, StringSplitOptions o = StringSplitOptions.None) => s.Split(new[] { c }, count, o);
        public static string[] Split(this string s, string sep, StringSplitOptions o = StringSplitOptions.None) => s.Split(new[] { sep }, o);
        public static bool TryAdd<K, V>(this Dictionary<K, V> d, K k, V v) { if (d.ContainsKey(k)) return false; d.Add(k, v); return true; }
        public static V GetValueOrDefault<K, V>(this Dictionary<K, V> d, K k, V def = default) => d.TryGetValue(k, out var v) ? v : def;
        public static void Deconstruct<K, V>(this KeyValuePair<K, V> kv, out K k, out V v) { k = kv.Key; v = kv.Value; }
        public static IEnumerable<T> TakeLast<T>(this IEnumerable<T> src, int n)
        {
            var l = new List<T>(src);
            return l.GetRange(System.Math.Max(0, l.Count - n), System.Math.Min(n, l.Count));
        }
        public static IEnumerable<T> SkipLast<T>(this IEnumerable<T> src, int n)
        {
            var l = new List<T>(src);
            return l.GetRange(0, System.Math.Max(0, l.Count - n));
        }
        public static T MaxBy<T, K>(this IEnumerable<T> src, Func<T, K> key) where K : IComparable<K>
        {
            bool any = false; T best = default; K bk = default;
            foreach (var x in src) { var k = key(x); if (!any || k.CompareTo(bk) > 0) { best = x; bk = k; any = true; } }
            return best;
        }
        public static T MinBy<T, K>(this IEnumerable<T> src, Func<T, K> key) where K : IComparable<K>
        {
            bool any = false; T best = default; K bk = default;
            foreach (var x in src) { var k = key(x); if (!any || k.CompareTo(bk) < 0) { best = x; bk = k; any = true; } }
            return best;
        }
        public static IEnumerable<T> DistinctBy<T, K>(this IEnumerable<T> src, Func<T, K> key)
        {
            var seen = new HashSet<K>();
            foreach (var x in src) if (seen.Add(key(x))) yield return x;
        }
    }
}

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}

namespace System
{
    /// <summary>Indice "da fine" (^1) per C# 8 su .NET Framework.</summary>
    public readonly struct Index : IEquatable<Index>
    {
        readonly int _v;
        public Index(int value, bool fromEnd = false) { if (value < 0) throw new ArgumentOutOfRangeException(nameof(value)); _v = fromEnd ? ~value : value; }
        Index(int v) { _v = v; }
        public static Index Start => new Index(0);
        public static Index End => new Index(~0);
        public static Index FromStart(int v) => new Index(v, false);
        public static Index FromEnd(int v) => new Index(v, true);
        public int Value => _v < 0 ? ~_v : _v;
        public bool IsFromEnd => _v < 0;
        public int GetOffset(int length) => _v < 0 ? length + _v + 1 : _v;
        public bool Equals(Index o) => _v == o._v;
        public override bool Equals(object o) => o is Index i && i._v == _v;
        public override int GetHashCode() => _v;
        public static implicit operator Index(int v) => FromStart(v);
        public override string ToString() => IsFromEnd ? "^" + Value : Value.ToString();
    }

    /// <summary>Intervallo (a..b) per C# 8 su .NET Framework.</summary>
    public readonly struct Range : IEquatable<Range>
    {
        public Index Start { get; }
        public Index End { get; }
        public Range(Index start, Index end) { Start = start; End = end; }
        public static Range StartAt(Index s) => new Range(s, Index.End);
        public static Range EndAt(Index e) => new Range(Index.Start, e);
        public static Range All => new Range(Index.Start, Index.End);
        public (int Offset, int Length) GetOffsetAndLength(int length)
        {
            int s = Start.GetOffset(length), e = End.GetOffset(length);
            if ((uint)e > (uint)length || (uint)s > (uint)e) throw new ArgumentOutOfRangeException(nameof(length));
            return (s, e - s);
        }
        public bool Equals(Range o) => o.Start.Equals(Start) && o.End.Equals(End);
        public override bool Equals(object o) => o is Range r && Equals(r);
        public override int GetHashCode() => Start.GetHashCode() * 31 + End.GetHashCode();
    }
}
