using System.Windows.Forms;

namespace TestDummy;

/// <summary>
/// Аргументы:
///   --windows N   сколько окон открыть (по умолчанию 1)
///   --ask         на закрытие спросить «Сохранить изменения?»
///   --tray        на закрытие только спрятать окно (как Telegram)
///   --ignore      не закрываться вовсе
///   --marker F    записать свои аргументы в файл F (по строке на аргумент)
///   --title T     заголовок окна
/// </summary>
internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var windows = 1;
        var ask = false;
        var tray = false;
        var ignore = false;
        string? marker = null;
        var title = "TestDummy";
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--windows": windows = int.Parse(args[++i]); break;
                case "--ask": ask = true; break;
                case "--tray": tray = true; break;
                case "--ignore": ignore = true; break;
                case "--marker": marker = args[++i]; break;
                case "--title": title = args[++i]; break;
            }
        }
        if (marker is not null)
            File.WriteAllLines(marker, args);

        Application.EnableVisualStyles();
        var forms = new List<Form>();
        for (var n = 0; n < windows; n++)
        {
            var form = new Form { Text = windows == 1 ? title : $"{title} {n + 1}", Width = 400, Height = 300, StartPosition = FormStartPosition.Manual, Left = 100 + n * 50, Top = 100 + n * 50 };
            form.FormClosing += (_, e) =>
            {
                // WinForms помечает WM_CLOSE от другой программы как TaskManagerClosing — для нас это «крестик».
                if (e.CloseReason is CloseReason.ApplicationExitCall or CloseReason.WindowsShutDown) return;
                if (ignore) { e.Cancel = true; return; }
                if (tray) { e.Cancel = true; form.Hide(); return; }
                if (ask)
                {
                    var answer = MessageBox.Show(form, "Сохранить изменения?", title, MessageBoxButtons.YesNoCancel);
                    if (answer == DialogResult.Cancel) e.Cancel = true;
                }
            };
            form.FormClosed += (_, _) =>
            {
                forms.Remove(form);
                if (forms.Count == 0) Application.ExitThread();
            };
            forms.Add(form);
            form.Show();
        }
        Application.Run();
        return 0;
    }
}
