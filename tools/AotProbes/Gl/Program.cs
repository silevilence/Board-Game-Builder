using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using SkiaSharp;

// Research only: no engine selection or gameplay is implemented here.
try
{
    var evidence=Path.GetFullPath(args.FirstOrDefault() ?? "gl-evidence");
    Directory.CreateDirectory(evidence);
    File.WriteAllText(Path.Combine(evidence,"runtime.txt"),$"dynamicCode={RuntimeFeature.IsDynamicCodeSupported}; os={Environment.OSVersion}; framework={System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}");
    for(var pass=0;pass<3;pass++)
    {
        using var game=new Probe(evidence,pass,args.Contains("--interactive"));
        game.Run();
    }
    File.WriteAllText(Path.Combine(evidence,"result.txt"),"PASS: three create/draw/resize/dispose cycles; inspect PNG and interactive input separately.");
    return 0;
}
catch(Exception error)
{
    Console.Error.WriteLine(error);
    return 1;
}

sealed class Probe : Game
{
    private readonly GraphicsDeviceManager graphics;
    private readonly string evidence;
    private readonly int pass;
    private readonly bool interactive;
    private SpriteBatch? batch;
    private Texture2D? picture,label;
    private int frames;
    private Vector2 position=new(40,100);
    public Probe(string evidence,int pass,bool interactive)
    {
        this.evidence=evidence;this.pass=pass;this.interactive=interactive;
        graphics=new(this) {PreferredBackBufferWidth=640,PreferredBackBufferHeight=400};
        Window.Title="NativeAOT GL probe — arrows / mouse / Escape";
        Window.AllowUserResizing=true;IsMouseVisible=true;
    }
    protected override void LoadContent()
    {
        batch=new(GraphicsDevice);
        using(var image=File.OpenRead(Path.Combine(AppContext.BaseDirectory,"marker.png")))picture=Texture2D.FromStream(GraphicsDevice,image);
        using var typeface=SKTypeface.FromFile(Path.Combine(AppContext.BaseDirectory,"font.otf"));
        using var font=new SKFont(typeface,26);
        const string text="原生图形验证：中文、图片、缩放";
        if(!font.ContainsGlyphs(text))throw new InvalidDataException("Missing Chinese font glyphs");
        using var bitmap=new SKBitmap(600,50,SKColorType.Rgba8888,SKAlphaType.Premul);
        using(var canvas=new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Transparent);
            using var paint=new SKPaint {Color=SKColors.White,IsAntialias=true};
            canvas.DrawText(text,4,34,SKTextAlign.Left,font,paint);
        }
        using var raster=SKImage.FromBitmap(bitmap);
        using var png=raster.Encode(SKEncodedImageFormat.Png,100);
        using var stream=new MemoryStream(png.ToArray());
        label=Texture2D.FromStream(GraphicsDevice,stream);
    }
    protected override void Update(GameTime time)
    {
        var keyboard=Keyboard.GetState();var mouse=Mouse.GetState();
        if(keyboard.IsKeyDown(Keys.Escape))Exit();
        if(keyboard.IsKeyDown(Keys.Right))position.X+=3;
        if(keyboard.IsKeyDown(Keys.Left))position.X-=3;
        if(mouse.LeftButton==ButtonState.Pressed)position=new(mouse.X,mouse.Y);
        if(++frames==8){graphics.PreferredBackBufferWidth=800;graphics.PreferredBackBufferHeight=500;graphics.ApplyChanges();}
        if(frames==20 && !interactive)Exit();
        base.Update(time);
    }
    private void Paint()
    {
        GraphicsDevice.Clear(new Color(22,35,48));
        batch!.Begin(transformMatrix:Matrix.CreateScale(GraphicsDevice.Viewport.Width/640f));
        batch.Draw(label!,new Vector2(10,20),Color.White);
        batch.Draw(picture!,new Rectangle((int)position.X,(int)position.Y,96,96),Color.White);
        batch.End();
    }
    protected override void Draw(GameTime time)
    {
        Paint();
        if(frames==4 || frames==12)
        {
            using var target=new RenderTarget2D(GraphicsDevice,GraphicsDevice.Viewport.Width,GraphicsDevice.Viewport.Height);
            try
            {
                GraphicsDevice.SetRenderTarget(target);Paint();
                using var output=File.Create(Path.Combine(evidence,$"frame-{pass}-{frames}.png"));
                target.SaveAsPng(output,target.Width,target.Height);
            }
            finally {GraphicsDevice.SetRenderTarget(null);}
            Paint();
        }
        base.Draw(time);
    }
    protected override void UnloadContent(){label?.Dispose();picture?.Dispose();batch?.Dispose();base.UnloadContent();}
}
