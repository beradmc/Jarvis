using System.Threading.Tasks;

namespace JarvisCSharp.Actions
{
    public interface IAction
    {
        string Name { get; }
        string Description { get; }

        /// <summary>
        /// Action'ı çalıştırır ve Gemini'ye iletilecek sonuç string'ini döndürür.
        /// Hata durumunda "Hata: ..." formatında mesaj döner.
        /// </summary>
        Task<string> ExecuteAsync(string payload);
    }
}
