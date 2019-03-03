WITH annotated AS (
    SELECT TOP(@Count) article_url FROM [annotations]
    WHERE user_id = @UserId AND num_sources IS NOT NULL
    ORDER BY updated_date DESC
)
SELECT articles_v5.article_url, title, site_name, plain_content 
FROM [articles_v5] INNER JOIN annotated 
ON articles_v5.article_url = annotated.article_url