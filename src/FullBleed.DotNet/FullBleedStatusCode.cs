namespace FullBleed.DotNet;

public enum FullBleedStatusCode : int
{
    Ok = 0,
    NullArgument = 1,
    InvalidUtf8 = 2,
    InvalidOptions = 3,
    RenderFailed = 4,
    IoFailed = 5,
    InvalidHandle = 6,
    SerializationFailed = 7,
    Panic = 255,
}
