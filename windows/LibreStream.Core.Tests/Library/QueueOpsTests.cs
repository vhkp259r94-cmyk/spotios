using LibreStream.Core;
using Xunit;

namespace LibreStream.Core.Tests;

public class QueueOpsTests
{
    private static bool Same(string a, string b) => a == b;

    [Fact]
    public void AddNextAndEndWithoutDuplicates()
    {
        var q = new List<string> { "1", "2", "3" }; var i = 0;
        QueueOps.Add(q, ref i, "9", true, Same);
        Assert.Equal(["1", "9", "2", "3"], q);
        QueueOps.Add(q, ref i, "9", false, Same);                 // ya estaba: se mueve al final
        Assert.Equal(["1", "2", "3", "9"], q);
        q = ["1", "2", "3"]; i = 1;
        QueueOps.Add(q, ref i, "1", true, Same);                  // una anterior: pasa a continuación y el índice se ajusta
        Assert.Equal(["2", "1", "3"], q); Assert.Equal(0, i);
        QueueOps.Add(q, ref i, "2", false, Same);                 // la actual no se duplica
        Assert.Equal(["2", "1", "3"], q);
    }

    [Fact]
    public void AddToEmptyQueueStartsIt()
    {
        var q = new List<string>(); var i = -1;
        QueueOps.Add(q, ref i, "a", true, Same);
        Assert.Equal(["a"], q); Assert.Equal(0, i);
    }

    [Fact]
    public void RemoveMoveAndClear()
    {
        var q = new List<string> { "1", "2", "3", "4" }; var i = 1;
        Assert.False(QueueOps.Remove(q, ref i, 1));               // la actual no se quita
        Assert.True(QueueOps.Remove(q, ref i, 0));                // quitar una anterior reajusta el índice
        Assert.Equal(["2", "3", "4"], q); Assert.Equal(0, i);
        Assert.True(QueueOps.Move(q, i, 2, -1));
        Assert.Equal(["2", "4", "3"], q);
        Assert.False(QueueOps.Move(q, i, 1, -1));                 // no pasa por encima de la actual
        Assert.False(QueueOps.Move(q, i, 2, 1));                  // ni se sale por abajo
        QueueOps.ClearUpcoming(q, i);
        Assert.Equal(["2"], q);
    }
}
