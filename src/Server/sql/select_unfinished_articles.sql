WITH unfinished_articles AS (
    SELECT article_url 
    FROM [annotations] 
    WHERE user_id =@UserId AND annotation IS NULL
)
SELECT articles_v5.article_url, title, site_name, plain_content FROM [articles_v5] 
INNER JOIN unfinished_articles ON articles_v5.article_url = unfinished_articles.article_url