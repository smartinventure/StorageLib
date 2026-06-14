namespace StorageLib.OpenSlide
{
    public class DeepZoomLayerInformation
    {
        public int Level { get; set; }

        public long LevelWidth { get; set; }

        public long LevelHeight { get; set; }

        public double LevelDownsample { get; set; }

        public int LayerDownsample { get; set; }

        public long LayerWidth { get; set; }

        public long LayerHeight { get; set; }
    }
}