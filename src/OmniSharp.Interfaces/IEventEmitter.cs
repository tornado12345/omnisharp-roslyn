namespace OmniSharp.Interfaces
{
    public interface IEventEmitter
    {
        void Emit(string kind, object args);
    }
}