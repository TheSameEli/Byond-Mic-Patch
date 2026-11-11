using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Windows.Forms;

[assembly: AssemblyTitle("BYOND Microphone Patch")]
[assembly: AssemblyVersion("1.0.0.0")]

[DataContract]
public class PatchEntry {
    [DataMember] public string version;
    [DataMember] public int offset;
    [DataMember] public string originalSha256;
    [DataMember] public string patchedSha256;
    [DataMember] public string originalBytes;
    [DataMember] public string patchedBytes;
}

public static class PatchEngine {
    private static readonly PatchEntry[] Entries = Load();
    private static PatchEntry[] Load() {
        using (Stream stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("manifest.json"))
            return (PatchEntry[])new DataContractJsonSerializer(typeof(PatchEntry[])).ReadObject(stream);
    }
    private static string Sha(byte[] bytes) {
        using (SHA256 hash = SHA256.Create())
            return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant();
    }
    private static byte[] Hex(string text) {
        byte[] bytes = new byte[text.Length / 2];
        for (int i = 0; i < bytes.Length; i++) bytes[i] = Convert.ToByte(text.Substring(i * 2, 2), 16);
        return bytes;
    }
    public static string ResolveDirectory(string directory) {
        directory = Path.GetFullPath(directory.Trim());
        if (File.Exists(Path.Combine(directory, "byondwin.dll"))) return directory;
        directory = Path.Combine(directory, "bin");
        if (!File.Exists(Path.Combine(directory, "byondwin.dll")))
            throw new InvalidOperationException("Не найден byondwin.dll. Выберите папку BYOND или BYOND\\bin.");
        return directory;
    }
    private static void RequireClosed(string directory) {
        int installerId = Process.GetCurrentProcess().Id;
        foreach (Process process in Process.GetProcesses()) {
            using (process) {
                if (process.Id == installerId) continue;
                string executable = null;
                try { executable = process.MainModule.FileName; } catch { }
                if (executable != null && String.Equals(Path.GetDirectoryName(executable), directory, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Закройте все приложения BYOND из выбранной папки, включая значок в трее.");
            }
        }
    }
    // A temporary file is verified before replacing the DLL. The original backup is retained.
    private static void Replace(string target, byte[] bytes, string expectedHash) {
        string temporary = target + ".microphone-patch.tmp";
        bool created = false;
        try {
            using (FileStream stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None)) {
                created = true;
                stream.Write(bytes, 0, bytes.Length);
                stream.Flush(true);
            }
            if (Sha(File.ReadAllBytes(temporary)) != expectedHash)
                throw new IOException("Не совпала контрольная сумма временного файла. DLL не изменена.");
            File.Replace(temporary, target, null);
        } finally {
            if (created && File.Exists(temporary)) File.Delete(temporary);
        }
    }
    public static string Execute(string directory, string mode) {
        directory = ResolveDirectory(directory);
        string target = Path.Combine(directory, "byondwin.dll");
        string backup = target + ".before-microphone-patch";
        byte[] data = File.ReadAllBytes(target);
        string hash = Sha(data);
        PatchEntry entry = Entries.SingleOrDefault(e => e.originalSha256 == hash || e.patchedSha256 == hash);
        if (entry == null) throw new InvalidOperationException("Эта DLL не поддерживается. Файл не изменён. Поддержка: BYOND 516.1685 и 516.1688 (Windows x86).");
        bool patched = hash == entry.patchedSha256;
        string status = "BYOND " + entry.version + ": " + (patched ? "патч установлен." : "оригинальная DLL, патч не установлен.");
        if (mode == "Status" || (mode == "Apply" && patched) || (mode == "Restore" && !patched)) return status;
        if (mode != "Apply" && mode != "Restore") throw new ArgumentException("Неизвестная операция.");
        RequireClosed(directory);
        if (mode == "Restore") {
            if (!File.Exists(backup)) throw new IOException("Оригинальная резервная копия не найдена. Откат не выполнен.");
            byte[] original = File.ReadAllBytes(backup);
            if (Sha(original) != entry.originalSha256) throw new IOException("Контрольная сумма резервной копии неверна. Откат не выполнен.");
            Replace(target, original, entry.originalSha256);
            return "BYOND " + entry.version + ": оригинальная DLL восстановлена.";
        }
        byte[] expected = Hex(entry.originalBytes), replacement = Hex(entry.patchedBytes);
        if (expected.Length != replacement.Length || entry.offset < 0 || entry.offset > data.Length - expected.Length)
            throw new InvalidOperationException("Неверное описание патча. DLL не изменена.");
        for (int i = 0; i < expected.Length; i++)
            if (data[entry.offset + i] != expected[i]) throw new InvalidOperationException("Обработчик разрешений отличается. DLL не изменена.");
        Array.Copy(replacement, 0, data, entry.offset, replacement.Length);
        if (Sha(data) != entry.patchedSha256) throw new InvalidOperationException("Неверная контрольная сумма патча. DLL не изменена.");
        if (File.Exists(backup)) {
            if (Sha(File.ReadAllBytes(backup)) != entry.originalSha256)
                throw new IOException("Существующая резервная копия отличается. Она сохранена без изменений.");
        } else File.Copy(target, backup, false);
        if (Sha(File.ReadAllBytes(backup)) != entry.originalSha256)
            throw new IOException("Не удалось проверить резервную копию. DLL не изменена.");
        Replace(target, data, entry.patchedSha256);
        return "Патч установлен. Разрешите микрофон в штатном запросе WebView2 при входе в игру.";
    }
}

public class PatchForm : Form {
    private readonly TextBox directory = new TextBox();
    private readonly Label status = new Label();
    public PatchForm() {
        Text = "BYOND Microphone Patch 1.0";
        Font = new Font("Segoe UI", 10);
        ClientSize = new Size(610, 300);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        Label description = new Label { Left = 16, Top = 14, Width = 575, Height = 60, Text = "Разрешение микрофона для Windows BYOND 516.1685 / 516.1688.\nПатч сохраняет запрос разрешения WebView2.\nЗапись звука автоматически не включается." };
        Controls.Add(description);
        directory.SetBounds(16, 85, 478, 28);
        directory.Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "BYOND", "bin");
        Controls.Add(directory);
        Button browse = new Button { Text = "Выбрать…", Left = 504, Top = 83, Width = 90, Height = 30 };
        browse.Click += delegate { using (FolderBrowserDialog dialog = new FolderBrowserDialog()) { dialog.Description = "Папка BYOND или BYOND\\bin"; if (Directory.Exists(directory.Text)) dialog.SelectedPath = directory.Text; if (dialog.ShowDialog(this) == DialogResult.OK) directory.Text = dialog.SelectedPath; } };
        Controls.Add(browse);
        AddButton("Проверить", 16, "Status");
        AddButton("Установить патч", 173, "Apply");
        AddButton("Восстановить BYOND", 354, "Restore");
        status.SetBounds(16, 176, 575, 104);
        status.Text = "Сначала закройте все приложения BYOND из выбранной папки.\nОригинальная DLL будет сохранена рядом с установленным BYOND.\nНеизвестные версии не изменяются.";
        Controls.Add(status);
    }
    private void AddButton(string text, int left, string mode) {
        Button button = new Button { Text = text, Left = left, Top = 130, Width = mode == "Restore" ? 240 : 165, Height = 32 };
        if (mode == "Status") button.Width = 145;
        button.Click += delegate {
            try {
                if (mode != "Status" && MessageBox.Show(this, mode == "Apply" ? "Применить патч к выбранной установке BYOND?" : "Восстановить оригинальную DLL из резервной копии?", Text, MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                status.ForeColor = SystemColors.ControlText;
                status.Text = PatchEngine.Execute(directory.Text, mode);
            } catch (Exception error) { status.ForeColor = Color.Firebrick; status.Text = error.Message; }
        };
        Controls.Add(button);
    }
    [STAThread]
    public static void Main() {
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.Run(new PatchForm());
    }
}
