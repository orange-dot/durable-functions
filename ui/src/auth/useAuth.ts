import { useContext } from 'react';
import { AuthContext, AuthContextType } from './DemoAuthProvider';

export function useAuth(): AuthContextType {
  const context = useContext(AuthContext);
  if (!context) {
    throw new Error('useAuth must be used within a DemoAuthProvider');
  }
  return context;
}
