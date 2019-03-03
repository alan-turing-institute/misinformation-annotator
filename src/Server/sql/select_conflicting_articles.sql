WITH annotated_by_user AS (
    SELECT article_url FROM [annotations]
    WHERE user_id = @UserId
),
conflicts AS (
    SELECT DISTINCT article_url
    FROM [annotations]
    WHERE article_url IN (
        SELECT article_url FROM [annotations]
        WHERE article_url NOT IN (SELECT * FROM annotated_by_user) AND num_sources IS NOT NULL
        GROUP BY article_url
        HAVING 
            COUNT(*) = 2 AND -- there are two annotations
            MAX(num_sources) - MIN(num_sources) > @Threshold -- difference between them is larger than threshold
    ) 
)
SELECT TOP(1) articles_v5.article_url, title, site_name, plain_content 
FROM [articles_v5] INNER JOIN conflicts 
ON articles_v5.article_url = conflicts.article_url