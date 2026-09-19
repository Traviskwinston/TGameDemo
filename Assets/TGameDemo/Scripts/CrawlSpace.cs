using UnityEngine;

namespace GameDemo
{
    /// <summary>
    /// Trigger volume for the crawl tunnel. Forces prone + camera zoom while inside.
    /// </summary>
    public class CrawlSpace : MonoBehaviour
    {
        void OnTriggerEnter(Collider other)
        {
            TlouTraversal trav = other.GetComponent<TlouTraversal>();
            if (trav != null)
            {
                trav.EnterCrawl(this);
            }
        }

        void OnTriggerExit(Collider other)
        {
            TlouTraversal trav = other.GetComponent<TlouTraversal>();
            if (trav != null)
            {
                trav.ExitCrawl(this);
            }
        }
    }
}
