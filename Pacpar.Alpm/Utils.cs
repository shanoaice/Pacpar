#pragma warning disable SYSLIB1054
using System.Runtime.InteropServices;

namespace Pacpar.Alpm;

public static class Utils
{
  [DllImport("libc", EntryPoint = "free", CallingConvention = CallingConvention.Cdecl)]
  internal extern unsafe static void CFree(void* ptr);
}
