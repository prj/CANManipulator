using SAE.J2534;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VehiCAL;

namespace CANManipulator {
    class Program {
        private static bool detailedLog = true;

        static void Main(string[] args) {
            try {
                using API api1 = APIFactory.GetAPI(@"C:\Windows\SysWOW64\op20pt32.dll");
                using API api2 = APIFactory.GetAPI(@"C:\Program Files (x86)\PEAK-System\PCAN-PassThru API\04.04\32\PCANPT32.dll");
                using Device device1 = api1.GetDevice();
                using Device device2 = api2.GetDevice();
                using Channel channel1 = device1.GetChannel(Protocol.CAN, Baud.CAN_500000, ConnectFlag.CAN_ID_BOTH);
                using Channel channel2 = device2.GetChannel(Protocol.CAN, Baud.CAN_500000, ConnectFlag.CAN_ID_BOTH);

                channel1.ClearMsgFilters();
                channel1.ClearRxBuffer();
                channel1.ClearTxBuffer();
                channel1.ClearPeriodicMsgs();

                channel2.ClearMsgFilters();
                channel2.ClearRxBuffer();
                channel2.ClearTxBuffer();
                channel2.ClearPeriodicMsgs();

                MessageFilter filter1 = new MessageFilter();
                filter1.PassAll();
                MessageFilter filter2 = new MessageFilter();
                filter2.PassAll();
                filter2.TxFlags |= TxFlag.CAN_29BIT_ID;

                channel1.StartMsgFilter(filter1);
                channel1.StartMsgFilter(filter2);
                channel2.StartMsgFilter(filter1);
                channel2.StartMsgFilter(filter2);

                channel1.DefaultTxTimeout = 0;
                channel2.DefaultTxTimeout = 0;

                List<Message> received1 = new List<Message>();
                List<Message> received2 = new List<Message>();
                ushort ctr = 0;

                HashSet<uint> AntiRepeat1 = new HashSet<uint>();
                HashSet<uint> AntiRepeat2 = new HashSet<uint>();

                Log("Connected!");
                Log("Detecting and muting broadcasts...");

                Stopwatch sw = Stopwatch.StartNew();
                SortedDictionary<uint, Stopwatch> elapsedTime = new SortedDictionary<uint, Stopwatch>();
                Stopwatch idsPrinted = Stopwatch.StartNew();

                Dictionary<uint, List<Func<byte[], byte[]>>> filterList = new Dictionary<uint, List<Func<byte[], byte[]>>>();

                filterList.Add(0x77E, new List<Func<byte[], byte[]>>());
                // F187 790D to 790C in Dash 0x17
                filterList[0x77E].Add((byte[] inb) => {
                    int pos = FindMemoryClone(Encoding.ASCII.GetBytes("790D"), inb, 1);
                    if (pos != -1) {
                        byte[] tocopy = Encoding.ASCII.GetBytes("790C");
                        Array.Copy(tocopy, 0, inb, pos, tocopy.Length);
                    }

                    return inb;
                });

                filterList.Add(0x7DD, new List<Func<byte[], byte[]>>());
                // F187 8W5035036C to 8W5035036 in Infotainment 0x5F
                filterList[0x7DD].Add((byte[] inb) => {
                    if (ArrayMatches(5, Encoding.ASCII.GetBytes("035036C"), inb)) {
                        inb[11] = 0x20;
                    }
                    return inb;
                });

                // F189 1329 to 0917 in Infotainment 0x5F
                filterList[0x7DD].Add((byte[] inb) => {
                    if (ArrayMatches(4, new byte[] { 0x07, 0x62, 0xF1, 0x89, 0x31, 0x33, 0x32, 0x39 }, inb)) {
                        byte[] tocopy = Encoding.ASCII.GetBytes("0917");
                        Array.Copy(tocopy, 0, inb, 8, tocopy.Length);
                    }
                    return inb;
                });

                int msgcnt = 0;

                FastTimer.Start(500);

                while (true) {
                    GetMessageResults results1 = channel1.GetMessages(10, 0);
                    GetMessageResults results2 = channel2.GetMessages(10, 0);

                    received1.Clear();
                    received2.Clear();

                    foreach (var rmsg in results1.Messages) {
                        if ((rmsg.FlagsAsInt & ((int)RxFlag.TX_INDICATION | (int)RxFlag.START_OF_MESSAGE | (int)RxFlag.TX_MSG_TYPE)) == 0) {
                            uint id = GetUInt32(rmsg.Data);
                            if (AntiRepeat1.Contains(id)) continue;
                            if (sw.IsRunning) {
                                AntiRepeat1.Add(id);
                                continue;
                            }
                            AntiRepeat2.Add(id);

                            byte[] filteredMsg = GetFilteredData(filterList, rmsg.Data);

                            if ((rmsg.FlagsAsInt & (int)RxFlag.CAN_29BIT_ID) > 0) {
                                received1.Add(new Message(filteredMsg, TxFlag.CAN_29BIT_ID));
                            } else {
                                received1.Add(new Message(filteredMsg, TxFlag.NONE));
                            }

                            msgcnt++;

                            elapsedTime[id] = Stopwatch.StartNew();

                            if (detailedLog) {
                                Log($"{ctr:X4} [TCRX] {GetHexString(rmsg.Data)}   ASCII: {GetFilteredASCII(rmsg.Data.TakeLast(7).ToArray())}");
                            }
                            ctr++;
                        }
                    }

                    foreach (var rmsg in results2.Messages) {
                        if ((rmsg.FlagsAsInt & ((int)RxFlag.TX_INDICATION | (int)RxFlag.START_OF_MESSAGE | (int)RxFlag.TX_MSG_TYPE)) == 0) {
                            uint id = GetUInt32(rmsg.Data);
                            if (AntiRepeat2.Contains(id)) continue;
                            if (sw.IsRunning) {
                                AntiRepeat2.Add(id);
                                continue;
                            }
                            AntiRepeat1.Add(id);

                            byte[] filteredMsg = GetFilteredData(filterList, rmsg.Data);

                            if ((rmsg.FlagsAsInt & (int)RxFlag.CAN_29BIT_ID) > 0) {
                                received2.Add(new Message(filteredMsg, TxFlag.CAN_29BIT_ID));
                            } else {
                                received2.Add(new Message(filteredMsg, TxFlag.NONE));
                            }

                            msgcnt++;

                            elapsedTime[id] = Stopwatch.StartNew();

                            if (detailedLog) {
                                Log($"{ctr:X4} [PCAN] {GetHexString(rmsg.Data)}   ASCII: {GetFilteredASCII(rmsg.Data.TakeLast(7).ToArray())}");
                            }
                            ctr++;
                        }
                    }

                    if (sw.ElapsedMilliseconds > 2000 && sw.IsRunning) {
                        Log($"OK, {AntiRepeat1.Count + AntiRepeat2.Count} ids muted.");
                        sw.Stop();
                    } else if (!sw.IsRunning && idsPrinted.ElapsedMilliseconds > 1000) {
                        StringBuilder sb = new StringBuilder();
                        sb.Append("Active IDs:");;

                        foreach (var id in elapsedTime.Keys) {
                            if (elapsedTime[id].ElapsedMilliseconds < 2000) {
                                sb.Append($" {id:X4}");
                            }
                        }

                        double msgpersec = (double)msgcnt / ((double)idsPrinted.ElapsedMilliseconds / 1000);

                        sb.Append($" {msgpersec:0.0} msg/sec");

                        Log(sb.ToString());

                        msgcnt = 0;
                        idsPrinted.Restart();
                    }


                    if (received1.Count > 0) {
                        try {
                            channel2.SendMessages(received1.ToArray());
                        } catch (J2534Exception) {

                        }
                    }

                    if (received2.Count > 0) {
                        try {
                            channel1.SendMessages(received2.ToArray());
                        } catch (J2534Exception) {

                        }
                    }
                    FastTimer.AwaitTimer();
                    FastTimer.ResetHandle();
                }
            } finally {
               FastTimer.Stop();
            }
        }

        private static void Log(string msg, bool nonewline = false) {
            if (nonewline) {
                Task.Run(() => Console.Write(msg));
            } else {
                Task.Run(() => Console.WriteLine(msg));
            }
        }

        public static string GetHexString(byte[] arr, string separator = " ") {
            return BitConverter.ToString(arr).Replace("-", separator);
        }

        public static UInt32 GetUInt32(IList<byte> arr, int offset = 0, bool HiLo = true) {
            if (HiLo) {
                return (UInt32)arr[offset + 3] | ((UInt32)arr[offset + 2] << 8) | ((UInt32)arr[offset + 1] << 16) | ((UInt32)arr[offset] << 24);
            } else {
                return (UInt32)arr[offset] | ((UInt32)arr[offset + 1] << 8) | ((UInt32)arr[offset + 2] << 16) | ((UInt32)arr[offset + 3] << 24);
            }
        }

        private static byte[] GetFilteredData(Dictionary<uint, List<Func<byte[], byte[]>>> filterList, byte[] msg) {
            uint id = GetUInt32(msg);
            byte[] newmsg = msg;
            if (filterList.TryGetValue(id, out var curList)) {
                foreach (var filter in curList) {
                    msg = filter.Invoke(msg);

                    if (!Enumerable.SequenceEqual(msg, newmsg)) {
                        Log($"Filter matched for ID {id:X4}!");
                        return msg;
                    }
                }
            }
            return newmsg;
        }

        public static int FindMemoryClone(byte[] needle, IList<byte> haystack, int align, bool inverse = false, int start = 0) {
            return FindMemoryClone(needle.Cast<byte?>().ToArray(), haystack, align, inverse, start);
        }

        /*
         * Find needle in haystack. null matches any byte. align > 1 forces search to be aligned.
         */
        public static int FindMemoryClone(byte?[] needle, IList<byte> haystack, int align, bool inverse = false, int start = 0) {
            if (!inverse) {
                for (int i = start; i < haystack.Count; i += align) {
                    bool match = true;
                    for (int j = 0; j < needle.Length; j++) {
                        if (i + j < haystack.Count) {
                            if (needle[j] != null && needle[j] != haystack[i + j]) {
                                match = false;
                                break;
                            }
                        } else {
                            match = false;
                            break;
                        }
                    }

                    if (match) {
                        return i;
                    }
                }

                return -1;
            } else {
                if (start == 0) {
                    start = haystack.Count - (haystack.Count % align) - needle.Length;
                }
                for (int i = start; i >= 0; i -= align) {
                    bool match = true;
                    for (int j = 0; j < needle.Length; j++) {
                        if (needle[j] != null && needle[j] != haystack[i + j]) {
                            match = false;
                            break;
                        }
                    }

                    if (match) {
                        return i;
                    }
                }

                return -1;
            }
        }

        public static string GetFilteredASCII(IList<byte> data, bool special = false) {
            List<byte> filtered = new List<byte>();
            for (int i = 0; i < data.Count; i++) {
                if (data[i] >= 0x20 && (data[i] <= 0x7F || special)) {
                    filtered.Add((byte)(data[i] & 0x7F));
                }
            }
            return Encoding.ASCII.GetString(filtered.ToArray()).Trim();
        }

        public static bool ArrayMatches(int pos, byte[] data, byte[] bin) {
            int i = 0;
            for (; i < data.Length && i + pos < bin.Length; i++) {
                if (data[i] != bin[pos + i]) {
                    return false;
                }
            }
            if (i != data.Length) {
                return false;
            }
            return true;
        }
    }
}
