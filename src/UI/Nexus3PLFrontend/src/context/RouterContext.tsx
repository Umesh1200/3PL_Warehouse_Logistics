import React, { createContext, useContext, useEffect, useState, useMemo } from 'react';

interface RouterContextType {
  path: string;
  queryParams: URLSearchParams;
  navigate: (to: string, options?: { replace?: boolean }) => void;
}

const RouterContext = createContext<RouterContextType | null>(null);

export const RouterProvider: React.FC<{ children: React.ReactNode; initialPath?: string }> = ({
  children,
  initialPath,
}) => {
  const [path, setPath] = useState<string>(() => {
    if (initialPath) return initialPath;
    if (typeof window !== 'undefined') {
      return window.location.pathname || '/';
    }
    return '/';
  });

  const [search, setSearch] = useState<string>(() => {
    if (typeof window !== 'undefined') {
      return window.location.search || '';
    }
    return '';
  });

  useEffect(() => {
    const handlePopState = () => {
      setPath(window.location.pathname || '/');
      setSearch(window.location.search || '');
    };

    window.addEventListener('popstate', handlePopState);
    return () => window.removeEventListener('popstate', handlePopState);
  }, []);

  const navigate = (to: string, options?: { replace?: boolean }) => {
    const [targetPath, targetSearch] = to.split('?');
    const newSearch = targetSearch ? `?${targetSearch}` : '';

    if (typeof window !== 'undefined') {
      if (options?.replace) {
        window.history.replaceState(null, '', to);
      } else {
        window.history.pushState(null, '', to);
      }
    }
    setPath(targetPath || '/');
    setSearch(newSearch);
  };

  const queryParams = useMemo(() => new URLSearchParams(search), [search]);

  return (
    <RouterContext.Provider value={{ path, queryParams, navigate }}>
      {children}
    </RouterContext.Provider>
  );
};

export const useRouter = (): RouterContextType => {
  const context = useContext(RouterContext);
  if (!context) {
    throw new Error('useRouter must be used within a RouterProvider');
  }
  return context;
};

export const Link: React.FC<
  React.AnchorHTMLAttributes<HTMLAnchorElement> & { to: string; className?: string }
> = ({ to, children, className, onClick, ...rest }) => {
  const { navigate } = useRouter();

  const handleClick = (e: React.MouseEvent<HTMLAnchorElement>) => {
    if (onClick) onClick(e);
    if (!e.defaultPrevented && e.button === 0 && !e.metaKey && !e.ctrlKey && !e.shiftKey && !e.altKey) {
      e.preventDefault();
      navigate(to);
    }
  };

  return (
    <a href={to} onClick={handleClick} className={className} {...rest}>
      {children}
    </a>
  );
};
