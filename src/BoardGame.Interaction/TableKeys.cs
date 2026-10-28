namespace BoardGame.View;
public static class TableKeys
{
    public static string? Named(string name,bool control=false,bool alt=false)
    {
        var key=name switch {"Escape"=>0x1B,"Enter" or "Return"=>0x0D,"F2"=>0x71,"F3"=>0x72,"F10"=>0x79,_=>0};
        if(name.Length==1) key=name[0];
        if(name.Length==2 && name[0]=='D' && char.IsAsciiDigit(name[1])) key=name[1];
        if(name.StartsWith("NumPad") && int.TryParse(name[6..],out var n) && n is >=0 and <=9) key=0x60+n;
        return Map(key,control,alt);
    }
    // Win32 virtual keys, separate from native delivery for modifier-path tests.
    public static string? Map(int key,bool control=false,bool alt=false)
    {
        if(alt) return null;
        if(control) return key switch {0x5A=>"undo",0x52=>"restart",_=>null};
        if(key is >=0x31 and <=0x38) return (key-0x30).ToString();
        if(key is >=0x61 and <=0x68) return (key-0x60).ToString();
        return key switch {0x30 or 0x60=>"cell",0x39 or 0x69=>"home",0x1B=>"cancel",0x0D=>"confirm",
            0x55 or 0x79=>"undo",0x48=>"hint",0x71=>"new-game",0x72=>"choose-seed",_=>null};
    }
}
