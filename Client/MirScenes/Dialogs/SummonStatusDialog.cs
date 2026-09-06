using Client.MirControls;
using Client.MirObjects;
using Client.MirGraphics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using SlimDX.Direct3D9;

namespace Client.MirScenes.Dialogs;

public sealed class SummonStatusDialog : IDisposable
{
    private readonly GameScene _scene;
    private readonly Dictionary<uint, SummonRow> _rows = new();

    public SummonStatusDialog(GameScene scene)
    {
        _scene = scene;
        scene.Disposing += (_, _) => Dispose();
    }

    private static bool IsSummon(MonsterObject monster) => GameScene.User != null &&
        !monster.Dead && monster.AI != 64 && monster.MasterObjectId == GameScene.User.ObjectID;

    internal MonsterObject HoveredTarget
    {
        get
        {
            if (MirControl.MouseControl is not SummonRow row || !_rows.ContainsValue(row) ||
                !row.Visible || !row.DisplayRectangle.Contains(CMain.MPoint)) return null;
            return MapControl.GetObject(row.ObjectID) is MonsterObject monster && IsSummon(monster)
                ? monster : null;
        }
    }

    public void Process()
    {
        foreach (uint id in _rows.Keys.ToArray())
        {
            if (MapControl.GetObject(id) is MonsterObject monster && IsSummon(monster)) continue;
            _rows[id].Dispose();
            _rows.Remove(id);
        }

        foreach (MonsterObject monster in MapControl.Objects.Values.OfType<MonsterObject>())
        {
            if (!IsSummon(monster) || _rows.ContainsKey(monster.ObjectID)) continue;
            _rows.Add(monster.ObjectID, new SummonRow(monster.ObjectID) { Parent = _scene });
        }

        int y = _scene.BuffsDialog.Location.Y + _scene.BuffsDialog.Size.Height + 10;
        int x = _scene.BuffsDialog.Location.X + _scene.BuffsDialog.Size.Width - 120;
        foreach (SummonRow row in _rows.Values)
        {
            row.Location = new Point(x, y);
            row.Update((MonsterObject)MapControl.GetObject(row.ObjectID));
            y += 30;
        }
    }

    public void Dispose()
    {
        foreach (SummonRow row in _rows.Values) row.Dispose();
        _rows.Clear();
    }

    private sealed class SummonRow : MirControl
    {
        public uint ObjectID { get; }
        private readonly MirLabel _name;
        private int _healthPercent = 100;
        private readonly MirLabel _percent;

        public SummonRow(uint objectID)
        {
            ObjectID = objectID;
            Size = new Size(120, 20);
            BackColour = Color.FromArgb(23, 19, 13);
            DrawControlTexture = true;
            _name = new MirLabel
            {
                Parent = this, Location = new Point(2, 2), Size = new Size(37, 16), NotControl = true,
                DrawFormat = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix,
                ForeColour = Color.FromArgb(239, 226, 187), OutLine = true, OutLineColour = Color.Black
            };
            _percent = new MirLabel
            {
                Parent = this, Location = new Point(42, 2), Size = new Size(76, 16), NotControl = true,
                DrawFormat = TextFormatFlags.SingleLine | TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter,
                ForeColour = Color.FromArgb(239, 226, 187), OutLine = true, OutLineColour = Color.Black
            };
        }

        protected override unsafe void CreateTexture()
        {
            if (TextureSize != Size) DisposeTexture();
            if (ControlTexture == null || ControlTexture.Disposed)
            {
                DXManager.ControlList.Add(this);
                ControlTexture = new Texture(DXManager.Device, Size.Width, Size.Height, 1,
                    Usage.None, Format.A8R8G8B8, Pool.Managed);
                TextureSize = Size;
            }

            var data = ControlTexture.LockRectangle(0, LockFlags.None);
            try
            {
                using var bitmap = new Bitmap(Size.Width, Size.Height, data.Pitch,
                    PixelFormat.Format32bppArgb, data.Data.DataPointer);
                using Graphics graphics = Graphics.FromImage(bitmap);
                graphics.SmoothingMode = SmoothingMode.None;
                graphics.Clear(BackColour);

                // The 40px name compartment and 80px health compartment share a bronze divider.
                using var nameFill = new LinearGradientBrush(new Rectangle(2, 2, 37, 16),
                    Color.FromArgb(43, 35, 23), Color.FromArgb(20, 18, 13), LinearGradientMode.Vertical);
                graphics.FillRectangle(nameFill, 2, 2, 37, 16);
                using var emptyHealth = new SolidBrush(Color.FromArgb(31, 13, 12));
                graphics.FillRectangle(emptyHealth, 42, 2, 76, 16);
                int width = 76 * _healthPercent / 100;
                if (width > 0)
                {
                    using var blood = new LinearGradientBrush(new Rectangle(42, 3, 76, 14),
                        Color.FromArgb(151, 32, 36), Color.FromArgb(65, 9, 13), LinearGradientMode.Vertical);
                    graphics.FillRectangle(blood, 42, 3, width, 14);
                    using var bloodHighlight = new Pen(Color.FromArgb(193, 65, 57));
                    graphics.DrawLine(bloodHighlight, 42, 3, 42 + width - 1, 3);
                    using var bloodShadow = new Pen(Color.FromArgb(45, 8, 10));
                    graphics.DrawLine(bloodShadow, 42, 17, 42 + width - 1, 17);
                }

                using var edge = new Pen(Color.FromArgb(45, 32, 17));
                using var bronze = new Pen(Color.FromArgb(133, 102, 51));
                using var highlight = new Pen(Color.FromArgb(192, 159, 91));
                using var shadow = new Pen(Color.FromArgb(72, 51, 25));
                graphics.DrawRectangle(edge, 0, 0, 119, 19);
                graphics.DrawRectangle(bronze, 1, 1, 117, 17);
                graphics.DrawLine(highlight, 1, 1, 118, 1);
                graphics.DrawLine(highlight, 1, 1, 1, 18);
                graphics.DrawLine(shadow, 2, 18, 118, 18);
                graphics.DrawLine(shadow, 118, 2, 118, 18);
                graphics.DrawLine(edge, 39, 2, 39, 17);
                graphics.DrawLine(bronze, 40, 2, 40, 17);
                graphics.DrawLine(shadow, 41, 2, 41, 17);

                // Small stepped corner fittings keep the trim legible at native size.
                foreach (Point corner in new[] { new Point(2, 2), new Point(115, 2), new Point(2, 15), new Point(115, 15) })
                {
                    graphics.DrawRectangle(bronze, corner.X, corner.Y, 2, 2);
                    graphics.DrawLine(highlight, corner.X, corner.Y, corner.X + 1, corner.Y);
                }
            }
            finally
            {
                ControlTexture.UnlockRectangle(0);
            }
            DXManager.Sprite.Flush();
            TextureValid = true;
        }

        public void Update(MonsterObject monster)
        {
            string name = monster.Name;
            string ownerSuffix = $"({GameScene.User.Name})";
            if (name.EndsWith(ownerSuffix, StringComparison.Ordinal)) name = name[..^ownerSuffix.Length];
            _name.Text = name;
            Hint = name;
            int health = Math.Clamp((int)monster.PercentHealth, 0, 100);
            if (_healthPercent != health)
            {
                _healthPercent = health;
                TextureValid = false;
                Redraw();
            }
            _percent.Text = $"{health}%";
        }
    }
}
