# OpenSlide Integration for FolderMagic

This integration provides support for exposing OpenSlide-compatible whole slide images as DeepZoom pyramids through FolderMagic's virtual file system.

## Features

- Expose slide files as DeepZoom tile pyramids in virtual directories
- Multiple format options (JPEG, PNG, BGRA) for tiles with configurable JPEG quality
- Customizable tile sizes for different performance requirements
- Automatic caching of slide metadata and generated tiles
- Comprehensive metadata about slide properties in JSON format
- Integration with MagicLink for easy configuration

## Usage

### Command Format

OpenSlide commands use the following format:

```
operation,sourcePath,virtualPath[,tileSize,format,jpegQuality]
```

Parameters:
- `operation`: The operation to perform (Create, Update, Delete)
- `sourcePath`: The path to the actual slide file
- `virtualPath`: The virtual path where the slide will be exposed
- `tileSize`: (Optional) The size of the tiles in pixels (default: 256, min: 50, max: 2048)
- `format`: (Optional) The tile format (JPEG, PNG, BGRA, default: JPEG)
- `jpegQuality`: (Optional) The JPEG quality (1-100, default: 75)

### Example Commands

```
Create,D:\slides\histology.svs,/mySlides/histo
Create,D:\slides\cytology.svs,/mySlides/cyto,512,PNG
Create,D:\slides\biopsy.svs,/mySlides/biopsy,256,JPEG,90
Update,D:\slides\histology.svs,/mySlides/histo,1024
Delete,D:\slides\cytology.svs,/mySlides/cyto
```

### Virtual Directory Structure

Each slide registered with OpenSlide will create a virtual directory structure as follows:

```
/mySlides/histo/                   # Root directory for the slide
    histo.dzi                      # DeepZoom descriptor XML
    metadata.json                  # Slide metadata in JSON format
    DZ_Files/                      # Directory containing the DeepZoom pyramid
        0/                         # Deepest zoom level (smallest size)
            0_0.jpeg               # Tile at column 0, row 0
            ...
        1/                         # Next zoom level
            ...
        ...
        N/                         # Highest level (full resolution)
            ...
```

### Integration in Code

To integrate the OpenSlide components with your FolderMagic instance:

```csharp
// Initialize MagicSetup
var setup = new MagicSetup();

// Register OpenSlide components
setup.RegisterOpenSlide();

// Use MagicLink to process OpenSlide commands
await setup.MagicLinkHandler.ProcessCustomCommandAsync("openslide", new List<string> {
    "Create,D:\\slides\\histology.svs,/mySlides/histo,256,JPEG,90"
});
```

## Implementation Notes

- This integration simulates tile generation for demonstration purposes
- In a production environment, it would use the OpenSlide native library
- The hybrid approach for file size estimation provides a balance between accuracy and performance
- Caching is used to improve performance for frequently accessed tiles and metadata

## Future Improvements

- Add actual OpenSlide library integration
- Support for region extraction (not just full slides)
- Enhanced error handling for corrupted slide files
- More efficient caching strategies for large slide collections
- Multi-threading support for parallel tile generation 