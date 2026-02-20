using Pacpar.Alpm.Bindings;

namespace Pacpar.Alpm.List;

public class AlpmDisposableList<T> : AlpmList<T> where T : IDisposable
{
  public unsafe AlpmDisposableList(delegate*<void*, T> factory) : base(factory)
  {
  }

  internal unsafe AlpmDisposableList(_alpm_list_t* alpmList, delegate*<void*, T> factory) : base(alpmList, factory)
  {
  }

  protected override void Dispose(bool disposing)
  {
    if ((!Disposed) && disposing)
    {
      foreach (var item in this)
      {
        item.Dispose();
      }
    }

    base.Dispose(disposing);
  }
}
