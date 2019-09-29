using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Forms;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;
using MessageBox = System.Windows.MessageBox;

namespace PFDAC
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private DataTable dgSource;
        public MainWindow()
        {
            InitializeComponent();
            dgSource = new DataTable();
            dgSource.Columns.Add("Drive", typeof(string));
            dgSource.Columns.Add("Status", typeof(string));
        }

        private void Btn_source_Click(object sender, RoutedEventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                DialogResult result = fbd.ShowDialog();
                if (result == System.Windows.Forms.DialogResult.OK
                    && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
                {
                    txt_source.Text = fbd.SelectedPath;
                }
            }
        }

        private void Btn_service_Click(object sender, RoutedEventArgs e)
        {
            if(btn_service.Content.ToString().Equals("Activate"))
            {
                backgroundWorker1_DoWork();
                btn_service.Content = "Deactivate";
            }
            else
            {
                
            }
        }

        private void DeviceInsertedEvent(object sender, EventArrivedEventArgs e)
        {
            string path = "";
            this.Dispatcher.Invoke((MethodInvoker)(() => path = txt_source.Text));
            List<string> list = getLetter();
            foreach (string itm in list)
            {
                bool exist = false;
                foreach (DataRow dr in dgSource.Rows)
                {
                    if (dr[0].ToString().Equals(itm))
                    {
                        exist = true;
                        break;
                    }
                }

                if (!exist)
                {
                    this.Dispatcher.Invoke(() => { dgSource.Rows.Add(itm,$"({DateTime.Now.ToLongTimeString()})Reading content"); });
                    Thread.Sleep(2000);
                    Task.Run(() => CopyDir(path,$@"{itm}\"));
                }
            }
        }

        private void DeviceRemovedEvent(object sender, EventArrivedEventArgs e)
        {
            List<string> list = getLetter();
            for (int i = dgSource.Rows.Count - 1; i >= 0; i--)
            {
                bool exist = false;
                DataRow dr = dgSource.Rows[i];
                foreach (string itm in list)
                {
                    if(itm.Equals(dr[0]))
                    {
                        exist = true;
                        break;
                    }
                }

                if (!exist)
                {
                    this.Dispatcher.Invoke(() =>
                    {
                        dr.Delete();
                        dgSource.AcceptChanges();
                    });
                    
                }
            }
        }

        private List<string> getLetter()
        {
            List<string> list = new List<string>();
            foreach (ManagementObject device in new ManagementObjectSearcher(@"SELECT * FROM Win32_DiskDrive WHERE InterfaceType LIKE 'USB%'").Get())
            {
                foreach (ManagementObject partition in new ManagementObjectSearcher(
                    "ASSOCIATORS OF {Win32_DiskDrive.DeviceID='" + device.Properties["DeviceID"].Value
                                                                 + "'} WHERE AssocClass = Win32_DiskDriveToDiskPartition").Get())
                {
                    foreach (ManagementObject disk in new ManagementObjectSearcher(
                        "ASSOCIATORS OF {Win32_DiskPartition.DeviceID='"
                        + partition["DeviceID"]
                        + "'} WHERE AssocClass = Win32_LogicalDiskToPartition").Get())
                    {
                        //Console.WriteLine("Drive letter " + disk["Name"]);
                        list.Add(disk["Name"].ToString());
                    }
                }
            }

            return list;
        }

        private void backgroundWorker1_DoWork()
        {
            WqlEventQuery insertQuery = new WqlEventQuery("SELECT * FROM __InstanceCreationEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_USBHub'");

            ManagementEventWatcher insertWatcher = new ManagementEventWatcher(insertQuery);
            insertWatcher.EventArrived += new EventArrivedEventHandler(DeviceInsertedEvent);
            insertWatcher.Start();

            WqlEventQuery removeQuery = new WqlEventQuery("SELECT * FROM __InstanceDeletionEvent WITHIN 2 WHERE TargetInstance ISA 'Win32_USBHub'");
            ManagementEventWatcher removeWatcher = new ManagementEventWatcher(removeQuery);
            removeWatcher.EventArrived += new EventArrivedEventHandler(DeviceRemovedEvent);
            removeWatcher.Start();
        }

        private void CopyDir(string root, string dest)
        {
            try
            {
                bool empty = false;
                this.Dispatcher.Invoke((MethodInvoker) (() => empty = cb_empty.IsChecked ?? false));
                if (empty)
                {
                    UpdateStatus(dest,$"Deleting file in {dest[0]}");
                    DirectoryInfo di = new DirectoryInfo(dest);
                    foreach (FileInfo file in di.EnumerateFiles())
                    {
                        File.SetAttributes(file.FullName, FileAttributes.Normal);
                        file.Delete();
                    }

                    foreach (DirectoryInfo dir in di.EnumerateDirectories())
                    {
                        dir.Delete(true);
                    }
                }
                UpdateStatus(dest, "Copy File");
                CloneDirectory(root, dest);
                EjectDrive(dest[0]);
                UpdateStatus(dest, "done,Safe to remove disk");
            }
            catch (Exception e)
            {
                MessageBox.Show(e.Message);
            }
        }

        private void UpdateStatus(string drive,string message)
        {
            this.Dispatcher.Invoke(() =>
            {
                foreach (DataRow dr in dgSource.Rows)
                {
                    if (dr[0].ToString().Equals(drive.Replace("\\", "")))
                        dr[1] = $"({DateTime.Now.ToLongTimeString()})"+message+dr[1].ToString();
                }
            });
        }


        private void CloneDirectory(string root, string dest)
        {
            foreach (var directory in Directory.GetDirectories(root))
            {
                string dirName = Path.GetFileName(directory);
                if (!Directory.Exists(Path.Combine(dest, dirName)))
                {
                    Directory.CreateDirectory(Path.Combine(dest, dirName));
                }
                CloneDirectory(directory, Path.Combine(dest, dirName));
            }

            foreach (var file in Directory.GetFiles(root))
            {
                FileInfo originalFile = new FileInfo(file);
                FileInfo destFile = new FileInfo(Path.Combine(dest, Path.GetFileName(file)));
                if(destFile.Exists)
                {
                    if (originalFile.LastWriteTime != destFile.LastWriteTime)
                        File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), true);
                }
                else
                    File.Copy(file, Path.Combine(dest, Path.GetFileName(file)));
            }
        }

        private void Dg_list_Loaded(object sender, RoutedEventArgs e)
        {
            dg_list.DataContext = dgSource.DefaultView;
        }

        //================================================================
        const uint GENERIC_READ = 0x80000000;
        const uint GENERIC_WRITE = 0x40000000;
        const int FILE_SHARE_READ = 0x1;
        const int FILE_SHARE_WRITE = 0x2;
        const int FSCTL_LOCK_VOLUME = 0x00090018;
        const int FSCTL_DISMOUNT_VOLUME = 0x00090020;
        const int IOCTL_STORAGE_EJECT_MEDIA = 0x2D4808;
        const int IOCTL_STORAGE_MEDIA_REMOVAL = 0x002D4804;

        void EjectDrive(char driveLetter)
        {
            string path = @"\\.\" + driveLetter + @":";
            IntPtr handle = CreateFile(path, GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, 0x3, 0, IntPtr.Zero);

            if ((long)handle == -1)
            {
                Console.WriteLine("Unable to open drive " + driveLetter);
                return;
            }

            int dummy = 0;

            DeviceIoControl(handle, IOCTL_STORAGE_EJECT_MEDIA, IntPtr.Zero, 0,
                IntPtr.Zero, 0, ref dummy, IntPtr.Zero);

            CloseHandle(handle);

            Console.WriteLine("OK to remove drive.");
        }
        [DllImport("kernel32", SetLastError = true)]
        private static extern IntPtr CreateFile
        (string filename, uint desiredAccess,
            uint shareMode, IntPtr securityAttributes,
            int creationDisposition, int flagsAndAttributes,
            IntPtr templateFile);
        [DllImport("kernel32")]
        private static extern int DeviceIoControl
        (IntPtr deviceHandle, uint ioControlCode,
            IntPtr inBuffer, int inBufferSize,
            IntPtr outBuffer, int outBufferSize,
            ref int bytesReturned, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);
    }
}
