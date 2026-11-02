using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using MouseBattery;

// Design-sheet host: publishes synthetic snapshots through the PRODUCTION
// publisher, then waits for the capture driver to acknowledge each cell.
// Compiled only by build-sheet.ps1; never part of the shipped exe.
// ASCII source only: the system csc has no reliable BOM-less UTF-8 path here,
// so CJK literals arrive as escapes.
class SheetHost {
    const string Wired = "\u6709\u7ebf";        // maps to transport 1 in TaskbarPublisher
    const string Bluetooth = "\u84dd\u7259";    // maps to transport 3
    const string CompanionProX = "PRO X 2 DEX";
    const string CompanionOffice = "Signature Plus M750 L";
    const long Now = 20000;

    static int Main(string[] args) {
        if(args.Length < 2) { Console.WriteLine("usage: SheetHost <cells.csv> <handshakeDir>"); return 2; }
        if(Process.GetProcessesByName("MouseBattery").Length != 0) {
            Console.WriteLine("STOP: the resident reader is running; it republishes every 500ms and would overwrite the sheet.");
            return 1;
        }
        string dir = args[1];
        string cellPath = Path.Combine(dir, "cell.txt");
        string ackPath = Path.Combine(dir, "ack.txt");
        string donePath = Path.Combine(dir, "done.txt");
        Directory.CreateDirectory(dir);
        DeleteQuietly(cellPath); DeleteQuietly(ackPath); DeleteQuietly(donePath);

        string[] lines = File.ReadAllLines(args[0]);
        int cells = 0;
        using(var publisher = new TaskbarPublisher()) using(var host = new NativeHost()) {
            for(int i = 0; i < lines.Length; i++) {
                string line = lines[i].Trim();
                if(line.Length == 0 || line.StartsWith("#")) continue;
                string[] c = line.Split(',');
                if(c.Length < 9) { Console.WriteLine("BAD CELL LINE: " + line); continue; }
                string id = c[0];
                int percent = ParseInt(c[1]);
                int charging = ParseInt(c[2]);
                int stale = ParseInt(c[3]);
                int gaming = ParseInt(c[4]);
                int transport = ParseInt(c[5]);
                string name = c[6];
                int companion = ParseInt(c[7]);
                int compStale = ParseInt(c[8]);
                int themeOverride = c.Length > 9 ? ParseInt(c[9]) : 0;

                var state = new DisplayState();
                var primary = Reading(name, "unit-primary", "hid#sheet-primary", percent, charging, gaming == 1, transport);
                state.Apply(new[]{ primary }, Now);
                if(companion == 1) {
                    bool otherGaming = gaming != 1;
                    var other = Reading(otherGaming ? CompanionProX : CompanionOffice, "unit-companion", "hid#sheet-companion",
                        78, 0, otherGaming, otherGaming ? 2 : 3);
                    state.Apply(new[]{ other }, compStale == 1 ? 0 : 19000);
                }
                if(stale == 2) state.MarkFailed(new[]{ primary.Path }, Now);
                publisher.MarkSuccess();
                publisher.Publish(state, Now, 0, false, themeOverride);

                string status = "";
                bool matched = false;
                for(int attempt = 0; attempt < 14; attempt++) {
                    Thread.Sleep(300);
                    try { host.Maintain(); }
                    catch(Exception e) { Console.WriteLine("ATTACH FAILED at " + id + ": " + e.Message); return 3; }
                    status = host.Status;
                    if(status.Contains("percent=" + percent.ToString(CultureInfo.InvariantCulture) + " ")
                        && status.Contains("kind=" + (gaming == 1 ? "gaming" : "office"))) { matched = true; break; }
                }
                File.WriteAllText(cellPath, id + "\t" + line + "\t" + (matched ? "CONFIRMED" : "UNCONFIRMED") + "\t" + status.Replace("\r", " ").Replace("\n", " "));
                Console.WriteLine("cell " + id + " " + (matched ? "confirmed" : "UNCONFIRMED") + " -> " + status.Replace("\n", " "));

                bool acked = false;
                for(int wait = 0; wait < 900; wait++) {
                    if(File.Exists(ackPath)) { acked = true; break; }
                    Thread.Sleep(100);
                }
                DeleteQuietly(ackPath); DeleteQuietly(cellPath);
                if(!acked) { Console.WriteLine("CAPTURE DRIVER TIMED OUT AT " + id); return 4; }
                cells++;
            }
        }
        File.WriteAllText(donePath, "cells=" + cells);
        Console.WriteLine("DONE cells=" + cells);
        return 0;
    }

    private static BatteryReading Reading(string name, string unit, string path, int percent, int charging, bool gaming, int transport) {
        var reading = BatteryReading.Parse(new byte[]{ (byte)percent, 4, (byte)charging, 0 }, name, unit,
            transport == 1 ? Wired : transport == 3 ? Bluetooth : "2.4G");
        reading.Path = path;
        reading.Gaming = gaming;
        return reading;
    }

    private static int ParseInt(string text) {
        int value;
        return int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) ? value : 0;
    }

    private static void DeleteQuietly(string path) {
        try { if(File.Exists(path)) File.Delete(path); }
        catch(IOException) { } catch(UnauthorizedAccessException) { }
    }
}
