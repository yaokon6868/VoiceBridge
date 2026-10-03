using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;
namespace VoiceBridge;

internal static class TrayArtwork
{
    [DllImport("user32.dll")] private static extern bool DestroyIcon(IntPtr icon);
    public static Icon Create()
    {
        using var bitmap=new Bitmap(32,32);
        using var g=Graphics.FromImage(bitmap);
        g.SmoothingMode=SmoothingMode.AntiAlias;
        using var background=new SolidBrush(Color.FromArgb(55,105,235));
        g.FillEllipse(background,1,1,30,30);
        using var pen=new Pen(Color.White,3) { StartCap=LineCap.Round, EndCap=LineCap.Round };
        for(var i=0;i<5;i++){var height=new[]{6,13,21,13,6}[i];var x=8+i*4;g.DrawLine(pen,x,16-height/2f,x,16+height/2f);}
        var handle=bitmap.GetHicon();
        try { using var original=Icon.FromHandle(handle);return (Icon)original.Clone(); }
        finally { DestroyIcon(handle); }
    }
}
internal sealed class TrayMenuRenderer : Forms.ToolStripProfessionalRenderer
{
    protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
    {
        using var brush=new SolidBrush(e.Item.Selected?Color.FromArgb(52,73,112):Color.FromArgb(25,32,48));
        e.Graphics.FillRectangle(brush,new Rectangle(Point.Empty,e.Item.Size));
    }
    protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
    { e.TextColor=Color.White; base.OnRenderItemText(e); }
}
