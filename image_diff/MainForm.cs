using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Drawing.Design;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Windows.Forms.Design;
using System.Threading;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace image_diff
{
    public partial class MainForm : Form
    {
        static uint[] crc_table = new uint[256];

        static void gen_crc_table()
        {
            uint crc_accum;
            for (uint i = 0; i < 256; i++)
            {
                crc_accum = i << 24;
                for (uint j = 0; j < 8; j++)
                {
                    if ((crc_accum & 0x80000000) != 0)
                        crc_accum = (crc_accum << 1) ^ 0x04c11db7; // Standard CRC-32 polynomial
                    else
                        crc_accum = crc_accum << 1;
                }
                crc_table[i] = crc_accum;
            }
        }

        static uint update_crc(
            uint crc_accum,
            byte[] data_blk)
        {
            foreach (var b in data_blk)
            {
                uint i = ((uint)(crc_accum >> 24) ^ b) & 0xFF;
                crc_accum = (crc_accum << 8) ^ crc_table[i];
            }
            crc_accum = ~crc_accum;
            return crc_accum;
        }

        public class Settings
        {
            [EditorAttribute(typeof(FolderNameEditor), typeof(UITypeEditor))]
            [DisplayName("Directory 1")]
            public string Directory1
            {
                get;
                set;
            }

            [EditorAttribute(typeof(FolderNameEditor), typeof(UITypeEditor))]
            [DisplayName("Directory 2")]
            public string Directory2
            {
                get;
                set;
            }
        }

        private Settings settings = new Settings();

        public MainForm()
        {
            InitializeComponent();

            gen_crc_table();

            propertyGrid.SelectedObject = settings;
        }

        static bool compare_images(string file1, string file2)
        {
            uint crc1 = 0;
            uint crc2 = 0;
            byte[] bytes1 = null;
            byte[] bytes2 = null;

            using (var image1 = (Bitmap)Image.FromFile(file1))
            {
                var bd1 = image1.LockBits(new Rectangle(0, 0, image1.Width, image1.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                bytes1 = new byte[bd1.Height * bd1.Stride];
                Marshal.Copy(bd1.Scan0, bytes1, 0, bytes1.Length);
                crc1 = update_crc(0, bytes1);
                image1.UnlockBits(bd1);
                image1.Dispose();
            }

            using (var image2 = (Bitmap)Image.FromFile(file2))
            {
                var bd2 = image2.LockBits(new Rectangle(0, 0, image2.Width, image2.Height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                bytes2 = new byte[bd2.Height * bd2.Stride];
                Marshal.Copy(bd2.Scan0, bytes2, 0, bytes2.Length);
                crc2 = update_crc(0, bytes2);
                image2.UnlockBits(bd2);
                image2.Dispose();
            }

            if (crc1 != crc2)
            {
                if (bytes1.Length != bytes2.Length)
                    return false;

                int pixel_count = 0;
                for (int i = 0; i < bytes1.Length / 3; ++i)
                {
                    if (bytes1[3 * i + 0] != bytes2[3 * i + 0] ||
                        bytes1[3 * i + 1] != bytes2[3 * i + 1] ||
                        bytes1[3 * i + 2] != bytes2[3 * i + 2])
                        ++pixel_count;
                }

                if (pixel_count > 50)
                    return false;
            }

            return true;
        }

        private void btCompare_Click(object sender, EventArgs e)
        {
            if (Directory.Exists(settings.Directory1) && Directory.Exists(settings.Directory2))
            {
                listBox.Items.Clear();

                Action<object> compare_thread = (object o) =>
                {
                    var files1 = Directory.GetFiles(settings.Directory1, "*.jpg");
                    
                    foreach (var f1 in files1)
                    {
                        var f2 = Path.Combine(settings.Directory2, Path.GetFileName(f1));

                        if (!compare_images(f1, f2))
                        {
                            listBox.BeginInvoke((MethodInvoker)delegate ()
                            {
                                listBox.Items.Add(string.Format("files {0} and {1} do not match", Path.GetFileName(f1), Path.GetFileName(f2)));
                            });
                        }
                    }

                    listBox.BeginInvoke((MethodInvoker)delegate ()
                    {
                        listBox.Items.Add("Finished");
                    });
                };

                var callback = new WaitCallback(compare_thread);
                ThreadPool.QueueUserWorkItem(callback);
            }
        }
    }
}
