using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Threading;

namespace DgusPlus
{
    // Заливка файлов DWIN_SET в дисплей T5L по UART — без SD-карты.
    // Файл N уходит во флеш по адресу N·256 КБ блоками по 32 КБ:
    //   1) блок кладётся в память переменных дисплея: VP 0x8000.., кадры 0x82 по 240 байт,
    //      на каждый дисплей отвечает 5A A5 03 82 4F 4B («OK») — это и управление потоком;
    //   2) команда в VP 0x00AA: 5A 02 <номер 32-КБ блока> 80 00 00 FF 00 00 00 00 —
    //      ядро переносит буфер во флеш и сбрасывает первый байт 0x00AA, когда готово;
    //   3) в конце перезагрузка дисплея: VP 0x0004 = 55 AA 5A 5A.

    // Канал к дисплею.
    interface ILink : IDisposable
    {
        void Write(byte[] buf, int off, int len);
        int Read(byte[] buf, int off, int len);   // 0 — за ~50 мс ничего не пришло
        void Discard();
    }

    class SerialLink : ILink
    {
        readonly SerialPort sp;
        public SerialLink(string port, int baud)
        {
            sp = new SerialPort(port, baud, Parity.None, 8, StopBits.One);
            sp.ReadTimeout = 50;
            sp.WriteTimeout = 3000;
            sp.Open();
        }
        public void Write(byte[] buf, int off, int len) { sp.Write(buf, off, len); }
        public int Read(byte[] buf, int off, int len)
        {
            try { return sp.Read(buf, off, len); }
            catch (TimeoutException) { return 0; }
        }
        public void Discard() { sp.DiscardInBuffer(); }
        public void Dispose() { try { if (sp.IsOpen) sp.Close(); } catch { } }
    }

    static class DisplayUpload
    {
        public delegate ILink LinkFactory(string port, int baud);
        public static LinkFactory OpenLink = delegate(string p, int b) { return new SerialLink(p, b); };

        const int Block = 32 * 1024;
        const int FileSlot = 256 * 1024;
        const int BufferVp = 0x8000;
        // Байт данных в одном кадре 0x82. T5L принимает в RAM не больше 248 байт за кадр
        // (так и у DgusDude); 250 молча обрезался при ответе «OK» — шрифт выходил с дырами.
        const int Chunk = 240;
        static readonly byte[] Ack = { 0x5A, 0xA5, 0x03, 0x82, 0x4F, 0x4B };
        static readonly string[] Uploadable = { ".bin", ".icl", ".hzk", ".dzk" };

        public class Job
        {
            public string State = "idle";          // running | done | error | cancelled
            public string Error;
            public List<string> Log = new List<string>();
            public long BytesDone, BytesTotal;
            public string CurrentFile;
            public DateTime Started, Finished;
            public volatile bool Cancel;
        }

        static Job current;
        public static Job Current { get { return current; } }

        public static bool IsUploadable(string name)
        {
            string ext = Path.GetExtension(name).ToLowerInvariant();
            return Array.IndexOf(Uploadable, ext) >= 0 && char.IsDigit(name[0]);
        }

        // Запуск в фоне; ход — через Current (окно DGUS+ и MCP upload_status).
        public static Job Start(string port, int baud, List<string> paths)
        {
            if (current != null && current.State == "running") throw new ArgumentException("An upload is already running.");
            if (paths.Count == 0) throw new ArgumentException("No files to upload.");
            Job job = new Job();
            job.State = "running";
            job.Started = DateTime.Now;
            foreach (string p in paths) job.BytesTotal += Pages(new FileInfo(p).Length) * (long)Block;
            current = job;
            Thread t = new Thread(delegate() { Run(job, port, baud, paths); });
            t.IsBackground = true;
            t.Name = "DGUS+ display upload";
            t.Start();
            return job;
        }

        static int Pages(long len) { return (int)Math.Max(1, (len + Block - 1) / Block); }

        static void Log(Job job, string s)
        {
            lock (job.Log) job.Log.Add(DateTime.Now.ToString("HH:mm:ss ") + s);
        }

        static void Run(Job job, string portName, int baud, List<string> paths)
        {
            ILink sp = null;
            try
            {
                sp = OpenLink(portName, baud);
                Log(job, portName + ", " + baud + " baud.");

                // Дисплей на связи? Читаем регистр флеша — он же понадобится для ожидания.
                // Если между нами и дисплеем контроллер, он включает прозрачный режим по первому же
                // кадру, но кадр, на котором он включился, может потеряться — поэтому три попытки.
                bool alive = false;
                for (int attempt = 0; attempt < 3 && !alive && !job.Cancel; attempt++)
                    alive = TryReadVp(sp, 0x00AA, 1, 2) != null;
                if (!alive)
                    throw new ArgumentException("The display does not respond. Check the port, baud rate and connection.");
                Log(job, "Display connected.");

                foreach (string path in paths)
                {
                    if (job.Cancel) break;
                    UploadFile(sp, job, path);
                }
                if (job.Cancel)
                {
                    job.State = "cancelled";
                    Log(job, "Stopped. The interrupted file must be uploaded again.");
                    return;
                }

                // Перезагрузка: подтверждения может и не быть — дисплей уходит в сброс.
                WriteFrame(sp, Frame82(0x0004, new byte[] { 0x55, 0xAA, 0x5A, 0x5A }));
                Log(job, "Done, the display is restarting.");
                job.State = "done";
            }
            catch (Exception ex)
            {
                job.Error = ex.Message;
                job.State = "error";
                Log(job, "Error: " + ex.Message);
                Plus.Log(ex);
            }
            finally
            {
                job.Finished = DateTime.Now;
                job.CurrentFile = null;
                if (sp != null) sp.Dispose();
            }
        }

        static void UploadFile(ILink sp, Job job, string path)
        {
            string name = Path.GetFileName(path);
            int id = ProjectFiles.IdOf(name);
            byte[] data = File.ReadAllBytes(path);
            int pages = Pages(data.Length);
            if (pages * Block > FileSlot * 64)
                throw new ArgumentException(name + " is too large.");
            job.CurrentFile = name;
            Log(job, name + ": ID " + id + ", " + (data.Length / 1024) + " KB, 32 KB blocks: " + pages + ".");

            byte[] page = new byte[Block];
            for (int p = 0; p < pages; p++)
            {
                if (job.Cancel) return;
                // Хвост последнего блока — 0xFF (как стёртая флеш), чтобы не записать
                // во флеш остатки предыдущего блока из буфера дисплея.
                for (int i = 0; i < Block; i++)
                {
                    int src = p * Block + i;
                    page[i] = src < data.Length ? data[src] : (byte)0xFF;
                }
                for (int off = 0; off < Block; off += Chunk)
                {
                    int n = Math.Min(Chunk, Block - off);
                    byte[] part = new byte[n];
                    Buffer.BlockCopy(page, off, part, 0, n);
                    WriteVp(sp, BufferVp + off / 2, part);
                    job.BytesDone += n;
                    if (job.Cancel) return;
                }
                int blockNo = id * (FileSlot / Block) + p;
                WriteVp(sp, 0x00AA, new byte[] {
                    0x5A, 0x02, (byte)(blockNo >> 8), (byte)blockNo,
                    (byte)(BufferVp >> 8), (byte)(BufferVp & 0xFF), 0x00, 0xFF, 0x00, 0x00, 0x00, 0x00 });
                WaitFlash(sp);
            }
            Log(job, name + ": written.");
        }


        static byte[] Frame82(int vp, byte[] data)
        {
            byte[] f = new byte[6 + data.Length];
            f[0] = 0x5A; f[1] = 0xA5; f[2] = (byte)(3 + data.Length); f[3] = 0x82;
            f[4] = (byte)(vp >> 8); f[5] = (byte)vp;
            Buffer.BlockCopy(data, 0, f, 6, data.Length);
            return f;
        }

        static void WriteFrame(ILink sp, byte[] f) { sp.Write(f, 0, f.Length); }

        // Запись с подтверждением; до трёх повторов (кадр мог потеряться по дороге).
        static void WriteVp(ILink sp, int vp, byte[] data)
        {
            byte[] f = Frame82(vp, data);
            for (int attempt = 0; attempt < 4; attempt++)
            {
                WriteFrame(sp, f);
                if (WaitFor(sp, Ack, 600) != null) return;
                sp.Discard();
            }
            throw new IOException("The display did not confirm writing VP 0x" + vp.ToString("X4") + ".");
        }

        static byte[] ReadVp(ILink sp, int vp, int words)
        {
            byte[] r = TryReadVp(sp, vp, words, 4);
            if (r == null)
                throw new IOException("The display does not respond to reading VP 0x" + vp.ToString("X4") +
                                      ". Check the port, baud rate and connection.");
            return r;
        }

        static byte[] TryReadVp(ILink sp, int vp, int words, int attempts)
        {
            byte[] f = { 0x5A, 0xA5, 0x04, 0x83, (byte)(vp >> 8), (byte)vp, (byte)words };
            byte[] head = { 0x5A, 0xA5, (byte)(4 + words * 2), 0x83, (byte)(vp >> 8), (byte)vp, (byte)words };
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                WriteFrame(sp, f);
                byte[] r = WaitFor(sp, head, 600, words * 2);
                if (r != null) return r;
                sp.Discard();
            }
            return null;
        }

        // Флеш занят, пока первый байт 0x00AA равен 0x5A.
        static void WaitFlash(ILink sp)
        {
            DateTime until = DateTime.Now.AddSeconds(5);
            while (DateTime.Now < until)
            {
                byte[] v = ReadVp(sp, 0x00AA, 1);
                if (v[0] != 0x5A) return;
                Thread.Sleep(10);
            }
            throw new IOException("The display did not finish writing to flash within 5 s.");
        }

        // Ждёт в потоке байтов последовательность pattern (+ extra байт после неё);
        // посторонние данные (события касаний и т. п.) пропускаются.
        static byte[] WaitFor(ILink sp, byte[] pattern, int timeoutMs, int extra)
        {
            List<byte> rx = new List<byte>();
            byte[] buf = new byte[512];
            DateTime until = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < until)
            {
                int n = ReadSome(sp, buf);
                for (int i = 0; i < n; i++) rx.Add(buf[i]);
                int at = IndexOf(rx, pattern);
                if (at >= 0 && rx.Count >= at + pattern.Length + extra)
                    return rx.GetRange(at + pattern.Length, extra).ToArray();
            }
            return null;
        }

        static byte[] WaitFor(ILink sp, byte[] pattern, int timeoutMs) { return WaitFor(sp, pattern, timeoutMs, 0); }

        static int ReadSome(ILink sp, byte[] buf)
        {
            return sp.Read(buf, 0, buf.Length);
        }

        static int IndexOf(List<byte> hay, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= hay.Count; i++)
            {
                int k = 0;
                while (k < needle.Length && hay[i + k] == needle[k]) k++;
                if (k == needle.Length) return i;
            }
            return -1;
        }
    }
}
