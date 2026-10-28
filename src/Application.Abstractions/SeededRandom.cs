namespace BoardGame.Kernel;

// Shared deterministic primitive; authority ownership remains in the board transaction.
public static class SeededRandom
{
    public static uint Next(ref uint state)
    {
        if(state==0) state=0x9E3779B9;
        state^=state<<13;state^=state>>17;state^=state<<5;
        return state;
    }
    public static int Index(ref uint state,int count)
    {
        if(count<=0) throw new ArgumentOutOfRangeException(nameof(count));
        return (int)(Next(ref state)%(uint)count);
    }
}
